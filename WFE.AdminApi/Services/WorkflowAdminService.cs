using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using WFE.AdminApi.Configuration;
using WFE.Test;
using WFE.Test.UseCases.ChangeRequest;
using WFE.Test.UseCases.LoanApproval;
using WFE.Test.UseCases.PaperlessReview;
using WFE.Test.UseCases.VendorRegistration;

namespace WFE.AdminApi.Services;

internal sealed class WorkflowAdminService : IWorkflowAdminService, IAsyncDisposable {
    private sealed record UseCaseProfile(
        string Key,
        string DefName,
        string StartEvent,
        string Folder,
        string DefinitionFile,
        string PolicyFile);

    private static readonly IReadOnlyDictionary<string, UseCaseProfile> UseCaseProfiles =
        new Dictionary<string, UseCaseProfile>(StringComparer.OrdinalIgnoreCase) {
            ["change-request"] = new(
                "change-request",
                ChangeRequestUseCaseSettings.DefinitionNameConst,
                "4000",
                "ChangeRequest",
                "definition.change_request.json",
                "policy.change_request.json"),
            ["loan-approval"] = new(
                "loan-approval",
                LoanApprovalUseCaseSettings.DefinitionNameConst,
                "2000",
                "LoanApproval",
                "definition.loan_approval.json",
                "policy.loan_approval.json"),
            ["paperless-review"] = new(
                "paperless-review",
                PaperlessReviewUseCaseSettings.DefinitionNameConst,
                "3000",
                "PaperlessReview",
                "definition.paperless_review.json",
                "policy.paperless_review.json"),
            ["vendor-registration"] = new(
                "vendor-registration",
                VendorRegistrationUseCaseSettings.DefinitionNameConst,
                "1000",
                "VendorRegistration",
                "definition.vendor_registration.json",
                "policy.vendor_registration.json")
        };

    private readonly WorkflowAdminOptions _options;
    private readonly AdapterGateway _agw;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _consumerInitLock = new(1, 1);
    private readonly SemaphoreSlim _runtimeInitLock = new(1, 1);

    private IWorkFlowEngine? _engine;
    private IConsumerAdminService? _consumerAdmin;
    private IWorkFlowConsumerService? _runtimeConsumer;
    private IServiceProvider? _runtimeProvider;
    private long _resolvedConsumerId;
    private bool _runtimeStarted;

    public WorkflowAdminService(IOptions<WorkflowAdminOptions> options, IAdapterGateway agw) {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _agw = agw as AdapterGateway ?? throw new ArgumentException("AdapterGateway implementation is required.", nameof(agw));
    }

    public async Task<LifeCycleInstanceData?> GetInstanceAsync(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var key = BuildInstanceKey(envCode, defName, entityId, instanceGuid);
        return await _engine!.GetInstanceDataAsync(key, ct);
    }

    public async Task<string?> GetTimelineJsonAsync(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var key = BuildInstanceKey(envCode, defName, entityId, instanceGuid);
        return await _engine!.GetTimelineJsonAsync(key, ct);
    }

    public async Task<IReadOnlyList<InstanceRefItem>> GetInstanceRefsAsync(
        int? envCode,
        string defName,
        LifeCycleInstanceFlag flags,
        int skip,
        int take,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(defName)) throw new ArgumentException("Definition name is required.", nameof(defName));
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        return await _engine!.GetInstanceRefsAsync(ResolveEnvCode(envCode), defName.Trim(), flags, normalizedSkip, normalizedTake, ct);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetEngineEntitiesAsync(
        string? defName,
        bool runningOnly,
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await _engine!.ListInstancesAsync(ResolveEnvCode(null), defName?.Trim(), runningOnly, normalizedSkip, normalizedTake, ct);
        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetPendingAcksAsync(
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await _engine!.ListPendingAcksAsync(ResolveEnvCode(null), normalizedSkip, normalizedTake, ct);
        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerWorkflowsAsync(
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await (await EnsureConsumerAdminAsync(ct)).ListWorkflowsAsync(normalizedSkip, normalizedTake, ct);
        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerInboxAsync(
        int? status,
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await (await EnsureConsumerAdminAsync(ct)).ListInboxAsync(status, normalizedSkip, normalizedTake, ct);
        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerOutboxAsync(
        int? status,
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await (await EnsureConsumerAdminAsync(ct)).ListOutboxAsync(status, normalizedSkip, normalizedTake, ct);
        return ToDictionaries(rows);
    }

    public async Task<Dictionary<string, object?>> GetSummaryAsync(CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var s = await _engine!.GetSummaryAsync(ResolveEnvCode(null), ct);
        var ca = await EnsureConsumerAdminAsync(ct);
        var inboxPending = await ca.CountPendingInboxAsync(ct);
        var outboxPending = await ca.CountPendingOutboxAsync(ct);
        return new Dictionary<string, object?> {
            ["envCode"] = _options.EnvCode,
            ["engineTotalInstances"] = s.TotalInstances,
            ["engineRunningInstances"] = s.RunningInstances,
            ["enginePendingAcks"] = s.PendingAcks,
            ["consumerPendingInbox"] = inboxPending,
            ["consumerPendingOutbox"] = outboxPending
        };
    }

    public async Task<Dictionary<string, object?>> GetHealthAsync(CancellationToken ct) {
        await EnsureInitializedAsync(ct);

        Dictionary<string, object?> engineCheck;
        var sw = Stopwatch.StartNew();
        try {
            var h = await _engine!.GetHealthAsync(ct);
            sw.Stop();
            engineCheck = new Dictionary<string, object?> {
                ["name"] = "engine",
                ["status"] = "healthy",
                ["responseTimeMs"] = sw.ElapsedMilliseconds,
                ["isMonitorRunning"] = h.IsMonitorRunning,
                ["monitorIntervalSeconds"] = h.MonitorInterval.TotalSeconds,
                ["consumerTtlSeconds"] = h.ConsumerTtlSeconds,
                ["totalConsumers"] = h.TotalConsumers,
                ["aliveConsumers"] = h.AliveConsumers,
                ["downConsumers"] = h.DownConsumers,
                ["dueLifecyclePending"] = h.DueLifecyclePendingCount,
                ["dueLifecycleDelivered"] = h.DueLifecycleDeliveredCount,
                ["dueHookPending"] = h.DueHookPendingCount,
                ["dueHookDelivered"] = h.DueHookDeliveredCount,
                ["staleInstances"] = h.DefaultStateStaleCount,
                ["runtimeStarted"] = _runtimeStarted
            };
        } catch (Exception ex) {
            sw.Stop();
            engineCheck = new Dictionary<string, object?> {
                ["name"] = "engine",
                ["status"] = "unhealthy",
                ["responseTimeMs"] = sw.ElapsedMilliseconds,
                ["error"] = ex.Message
            };
        }

        var consumerCheck = await PingAdapterAsync(_options.ConsumerAdapterKey, "consumer_db", ct);

        var allHealthy = (string?)engineCheck["status"] == "healthy"
                      && (string?)consumerCheck["status"] == "healthy";

        return new Dictionary<string, object?> {
            ["status"] = allHealthy ? "healthy" : "unhealthy",
            ["checkedAt"] = DateTimeOffset.UtcNow,
            ["checks"] = new[] { engineCheck, consumerCheck }
        };
    }

    public async Task<Dictionary<string, object?>> EnsureHostInitializedAsync(CancellationToken ct) {
        var wasRuntimeStarted = _runtimeStarted;
        await EnsureInitializedAsync(ct);

        return new Dictionary<string, object?> {
            ["status"] = "ok",
            ["checkedAt"] = DateTimeOffset.UtcNow,
            ["engineInitialized"] = _engine != null,
            ["runtimeStarted"] = _runtimeStarted,
            ["alreadyRunning"] = wasRuntimeStarted,
            ["envCode"] = _options.EnvCode,
            ["consumerId"] = _resolvedConsumerId > 0 ? _resolvedConsumerId : null
        };
    }

    public Task<IReadOnlyList<string>> GetTestUseCasesAsync(CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var keys = UseCaseProfiles.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> CreateTestEntitiesAsync(
        string useCase,
        int count,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(useCase)) throw new ArgumentException("useCase is required.", nameof(useCase));
        if (count < 1) return Array.Empty<Dictionary<string, object?>>();
        if (count > 1000) throw new ArgumentException("count is too high. max=1000", nameof(count));

        await EnsureInitializedAsync(ct);
        if (!UseCaseProfiles.TryGetValue(useCase.Trim(), out var profile))
            throw new ArgumentException($"Unknown use-case '{useCase}'.", nameof(useCase));

        var results = new List<Dictionary<string, object?>>(count);
        for (var i = 0; i < count; i++) {
            ct.ThrowIfCancellationRequested();
            var entityId = Guid.NewGuid().ToString("N");
            var trigger = await _engine!.TriggerAsync(new LifeCycleTriggerRequest {
                EnvCode = _options.EnvCode,
                DefName = profile.DefName,
                EntityId = entityId,
                Event = profile.StartEvent,
                Actor = "wfe.adminapi.test",
                AckRequired = true,
                Payload = new Dictionary<string, object> {
                    ["source"] = "WFE.AdminApi",
                    ["useCase"] = profile.Key,
                    ["requestIndex"] = i + 1
                },
                Metadata = $"adminapi;test;{profile.Key}"
            }, ct);

            results.Add(new Dictionary<string, object?> {
                ["useCase"] = profile.Key,
                ["entityId"] = entityId,
                ["applied"] = trigger.Applied,
                ["instanceId"] = trigger.InstanceId,
                ["instanceGuid"] = trigger.InstanceGuid,
                ["lifeCycleId"] = trigger.LifeCycleId,
                ["reason"] = trigger.Reason
            });
        }

        return results;
    }

    public async ValueTask DisposeAsync() {
        if (_runtimeConsumer != null) {
            try { await _runtimeConsumer.StopAsync(CancellationToken.None); } catch { }
        }

        if (_engine != null) {
            try { await _engine.StopMonitorAsync(CancellationToken.None); } catch { }
        }

        if (_runtimeProvider is IAsyncDisposable asyncProvider) {
            try { await asyncProvider.DisposeAsync(); } catch { }
        } else if (_runtimeProvider is IDisposable provider) {
            try { provider.Dispose(); } catch { }
        }

        if (_engine is IAsyncDisposable disposableEngine) {
            try { await disposableEngine.DisposeAsync(); } catch { }
        }

        _initLock.Dispose();
        _consumerInitLock.Dispose();
        _runtimeInitLock.Dispose();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) {
        if (_engine == null) {
            await _initLock.WaitAsync(ct);
            try {
                if (_engine == null) {
                    var defaults = new UseSettingsBase();
                    var engineMaker = new WorkFlowEngineMaker().WithAdapterKey(_options.EngineAdapterKey);
                    engineMaker.Options = new WorkFlowEngineOptions {
                        MonitorInterval = defaults.MonitorInterval,
                        AckPendingResendAfter = defaults.AckPendingResendAfter,
                        AckDeliveredResendAfter = defaults.AckDeliveredResendAfter,
                        MaxRetryCount = defaults.MaxRetryCount,
                        ConsumerTtlSeconds = defaults.ConsumerTtlSeconds,
                        ConsumerDownRecheckSeconds = defaults.ConsumerDownRecheckSeconds,
                        ResolveConsumers = (ty, defVersionId, token) => {
                            token.ThrowIfCancellationRequested();
                            if (_resolvedConsumerId <= 0) return Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>());
                            return Task.FromResult<IReadOnlyList<long>>(new[] { _resolvedConsumerId });
                        }
                    };
                    _engine = await engineMaker.Build(_agw);
                }
            } finally {
                _initLock.Release();
            }
        }

        await EnsureRuntimeStartedAsync(ct);
    }

    private async Task EnsureRuntimeStartedAsync(CancellationToken ct) {
        if (_runtimeStarted) return;

        await _runtimeInitLock.WaitAsync(ct);
        try {
            if (_runtimeStarted) return;
            if (_engine == null) throw new InvalidOperationException("Engine is not initialized.");

            await _engine.RegisterEnvironmentAsync(_options.EnvCode, _options.EnvDisplayName, ct);
            _resolvedConsumerId = await _engine.RegisterConsumerAsync(_options.EnvCode, _options.ConsumerGuid, ct);

            var changeSettings = BuildSettings(new ChangeRequestUseCaseSettings());
            var loanSettings = BuildSettings(new LoanApprovalUseCaseSettings());
            var paperlessSettings = BuildSettings(new PaperlessReviewUseCaseSettings());
            var vendorSettings = BuildSettings(new VendorRegistrationUseCaseSettings());

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IWorkFlowEngine>(_engine);
            serviceCollection.AddSingleton(changeSettings);
            serviceCollection.AddSingleton(loanSettings);
            serviceCollection.AddSingleton(paperlessSettings);
            serviceCollection.AddSingleton(vendorSettings);
            serviceCollection.AddTransient<ChangeRequestWrapper>();
            serviceCollection.AddTransient<LoanApprovalWrapper>();
            serviceCollection.AddTransient<PaperlessReviewWrapper>();
            serviceCollection.AddTransient<VendorRegistrationWrapper>();

            _runtimeProvider = serviceCollection.BuildServiceProvider();

            var feed = new InProcessEngineProxy(_engine);
            var consumerOptions = new ConsumerServiceOptions {
                EnvCode = _options.EnvCode,
                ConsumerGuid = _options.ConsumerGuid,
                BatchSize = changeSettings.ConsumerBatchSize,
                PollInterval = changeSettings.ConsumerPollInterval,
                HeartbeatInterval = changeSettings.ConsumerHeartbeatInterval
            };

            var consumerMaker = new WorkFlowConsumerMaker()
                .WithAdapterKey(_options.ConsumerAdapterKey)
                .WithProvider(_runtimeProvider);
            consumerMaker.EngineProxy = feed;
            consumerMaker.Options = consumerOptions;

            _runtimeConsumer = await consumerMaker.Build(_agw);
            _runtimeConsumer.RegisterAssembly(typeof(ChangeRequestWrapper).Assembly);
            _runtimeConsumer.NoticeRaised += n => {
                Console.WriteLine($"[NOTICE:{n.Kind}] {n.Code} :: {n.Message}" +
                    (n.Exception != null ? $" ex={n.Exception.GetType().Name}: {n.Exception.Message}" : string.Empty));
                return Task.CompletedTask;
            };

            await ImportUseCasesAsync(ct);
            await _engine.StartMonitorAsync(ct);
            await _runtimeConsumer.StartAsync(ct);
            _runtimeStarted = true;
        } finally {
            _runtimeInitLock.Release();
        }
    }

    private async Task ImportUseCasesAsync(CancellationToken ct) {
        for (var i = 0; i < UseCaseProfiles.Count; i++) {
            ct.ThrowIfCancellationRequested();
            var profile = UseCaseProfiles.Values.ElementAt(i);

            var definitionPath = ResolveUseCaseFilePath(profile.Folder, profile.DefinitionFile);
            var policyPath = ResolveUseCaseFilePath(profile.Folder, profile.PolicyFile);

            var definitionJson = await File.ReadAllTextAsync(definitionPath, ct);
            var policyJson = await File.ReadAllTextAsync(policyPath, ct);

            var defVersionId = await _engine!.ImportDefinitionJsonAsync(_options.EnvCode, _options.EnvDisplayName, definitionJson, ct);
            var policyId = await _engine.ImportPolicyJsonAsync(_options.EnvCode, _options.EnvDisplayName, policyJson, ct);
            await _engine.InvalidateAsync(_options.EnvCode, profile.DefName, ct);

            Console.WriteLine($"[TEST-RUNTIME] Imported use-case={profile.Key} defVersionId={defVersionId} policyId={policyId}");
        }
    }

    private string ResolveUseCaseFilePath(string folder, string fileName) {
        var candidateRoots = new List<string>(6);
        if (!string.IsNullOrWhiteSpace(_options.UseCasesRootPath)) {
            candidateRoots.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.UseCasesRootPath)));
            candidateRoots.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), _options.UseCasesRootPath)));
        }

        candidateRoots.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WFE.Lib", "UseCases")));
        candidateRoots.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "WFE.Lib", "UseCases")));
        candidateRoots.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WFE.Test", "UseCases")));
        candidateRoots.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "WFE.Test", "UseCases")));

        for (var i = 0; i < candidateRoots.Count; i++) {
            var candidate = Path.Combine(candidateRoots[i], folder, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Unable to locate use-case file '{folder}/{fileName}'. Checked roots: {string.Join(" | ", candidateRoots)}");
    }

    private T BuildSettings<T>(T settings) where T : UseSettingsBase {
        settings.EnvCode = _options.EnvCode;
        settings.EnvDisplayName = _options.EnvDisplayName;
        settings.ConsumerGuid = _options.ConsumerGuid;
        settings.ENGINE_DBNAME = _options.EngineAdapterKey;
        settings.CONSUMER_DBNAME = _options.ConsumerAdapterKey;
        settings.ConfirmationTimeout = TimeSpan.FromSeconds(Math.Max(0, _options.ConfirmationTimeoutSeconds));
        return settings;
    }

    private async Task<IConsumerAdminService> EnsureConsumerAdminAsync(CancellationToken ct) {
        if (_consumerAdmin != null) return _consumerAdmin;
        await _consumerInitLock.WaitAsync(ct);
        try {
            if (_consumerAdmin != null) return _consumerAdmin;
            var maker = new WorkFlowConsumerMaker().WithAdapterKey(_options.ConsumerAdapterKey);
            _consumerAdmin = await maker.BuildAdmin(_agw);
            return _consumerAdmin;
        } finally {
            _consumerInitLock.Release();
        }
    }

    private async Task<Dictionary<string, object?>> PingAdapterAsync(string adapterKey, string checkName, CancellationToken ct) {
        var sw = Stopwatch.StartNew();
        try {
            await _agw.ScalarAsync<int>(adapterKey, "SELECT 1", new DbExecutionLoad(ct));
            sw.Stop();
            return new Dictionary<string, object?> {
                ["name"] = checkName,
                ["status"] = "healthy",
                ["responseTimeMs"] = sw.ElapsedMilliseconds
            };
        } catch (Exception ex) {
            sw.Stop();
            return new Dictionary<string, object?> {
                ["name"] = checkName,
                ["status"] = "unhealthy",
                ["responseTimeMs"] = sw.ElapsedMilliseconds,
                ["error"] = ex.Message
            };
        }
    }

    private LifeCycleInstanceKey BuildInstanceKey(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid) {
        if (!string.IsNullOrWhiteSpace(instanceGuid)) {
            return new LifeCycleInstanceKey {
                InstanceGuid = instanceGuid.Trim()
            };
        }

        if (string.IsNullOrWhiteSpace(defName)) throw new ArgumentException("Definition name is required when instanceGuid is not supplied.", nameof(defName));
        if (string.IsNullOrWhiteSpace(entityId)) throw new ArgumentException("Entity id is required when instanceGuid is not supplied.", nameof(entityId));

        return new LifeCycleInstanceKey {
            EnvCode = ResolveEnvCode(envCode),
            DefName = defName.Trim(),
            EntityId = entityId.Trim()
        };
    }

    private int ResolveEnvCode(int? envCode)
        => envCode.GetValueOrDefault(_options.EnvCode);

    private (int skip, int take) NormalizePaging(int skip, int take) {
        var normalizedSkip = skip < 0 ? 0 : skip;
        var fallbackTake = _options.DefaultTake > 0 ? _options.DefaultTake : 50;
        var normalizedTake = take <= 0 ? fallbackTake : take;
        var maxTake = _options.MaxTake > 0 ? _options.MaxTake : 500;
        if (normalizedTake > maxTake) normalizedTake = maxTake;
        return (normalizedSkip, normalizedTake);
    }

    private static IReadOnlyList<Dictionary<string, object?>> ToDictionaries(DbRows rows) {
        var result = new List<Dictionary<string, object?>>(rows.Count);
        for (var i = 0; i < rows.Count; i++) {
            var row = rows[i];
            var item = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in row) {
                item[entry.Key] = entry.Value == DBNull.Value ? null : entry.Value;
            }
            result.Add(item);
        }
        return result;
    }
}
