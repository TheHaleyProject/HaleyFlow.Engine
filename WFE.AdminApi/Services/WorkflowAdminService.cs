using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using WFE.AdminApi.Configuration;

namespace WFE.AdminApi.Services;

internal sealed class WorkflowAdminService : IWorkflowAdminService, IAsyncDisposable {
    private const string EngineAdapterKey = "lce_test";
    private const string ConsumerAdapterKey = "lcc_test";

    private readonly WorkflowAdminOptions _options;
    private readonly AdapterGateway _agw;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _consumerInitLock = new(1, 1);

    private IWorkFlowEngine? _engine;
    private IConsumerAdminService? _consumerAdmin;

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

        // Engine health — delegated to WorkFlowEngine.GetHealthAsync → EngineCare + AckManager DAL.
        // No raw SQL here; EngineCareDAL owns all engine-side health queries.
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
                ["staleInstances"] = h.DefaultStateStaleCount
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

        // Consumer DB ping — admin-side concern only (no consumer-side service here).
        var consumerCheck = await PingAdapterAsync(ConsumerAdapterKey, "consumer_db", ct);

        var allHealthy = (string?)engineCheck["status"] == "healthy"
                      && (string?)consumerCheck["status"] == "healthy";

        return new Dictionary<string, object?> {
            ["status"] = allHealthy ? "healthy" : "unhealthy",
            ["checkedAt"] = DateTimeOffset.UtcNow,
            ["checks"] = new[] { engineCheck, consumerCheck }
        };
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

    public async ValueTask DisposeAsync() {
        if (_engine is IAsyncDisposable disposableEngine) {
            try { await disposableEngine.DisposeAsync(); } catch { }
        }
        _initLock.Dispose();
        _consumerInitLock.Dispose();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) {
        if (_engine != null) return;

        await _initLock.WaitAsync(ct);
        try {
            if (_engine != null) return;
            var engineMaker = new WorkFlowEngineMaker().WithAdapterKey(EngineAdapterKey);
            _engine = await engineMaker.Build(_agw);
        } finally {
            _initLock.Release();
        }
    }

    private async Task<IConsumerAdminService> EnsureConsumerAdminAsync(CancellationToken ct) {
        if (_consumerAdmin != null) return _consumerAdmin;
        await _consumerInitLock.WaitAsync(ct);
        try {
            if (_consumerAdmin != null) return _consumerAdmin;
            var maker = new WorkFlowConsumerMaker().WithAdapterKey(ConsumerAdapterKey);
            _consumerAdmin = await maker.BuildAdmin(_agw);
            return _consumerAdmin;
        } finally {
            _consumerInitLock.Release();
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
