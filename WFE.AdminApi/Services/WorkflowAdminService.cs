using Haley.Abstractions;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using Microsoft.Extensions.Options;
using WFE.AdminApi.Configuration;
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
    private readonly IWorkFlowEngineService _engineService;
    private readonly IWorkFlowEngineAccessor _engineAccessor;
    private readonly IWorkFlowConsumerInitiatorService _consumerInitiator;
    private readonly AdapterGateway _agw;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _consumerInitLock = new(1, 1);

    private IWorkFlowEngine? _engine;
    private IConsumerAdminService? _consumerAdmin;
    private bool _runtimeStarted;

    public WorkflowAdminService(
        IOptions<WorkflowAdminOptions> options,
        IWorkFlowEngineService engineService,
        IWorkFlowEngineAccessor engineAccessor,
        IWorkFlowConsumerInitiatorService consumerInitiator,
        IAdapterGateway agw) {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _engineService = engineService ?? throw new ArgumentNullException(nameof(engineService));
        _engineAccessor = engineAccessor ?? throw new ArgumentNullException(nameof(engineAccessor));
        _consumerInitiator = consumerInitiator ?? throw new ArgumentNullException(nameof(consumerInitiator));
        _agw = agw as AdapterGateway ?? throw new ArgumentException("AdapterGateway implementation is required.", nameof(agw));
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerWorkflowsAsync(int skip, int take, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await (await EnsureConsumerAdminAsync(ct)).ListWorkflowsAsync(normalizedSkip, normalizedTake, ct);
        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerInboxAsync(int? status, int skip, int take, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await (await EnsureConsumerAdminAsync(ct)).ListInboxAsync(status, normalizedSkip, normalizedTake, ct);
        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerOutboxAsync(int? status, int skip, int take, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await (await EnsureConsumerAdminAsync(ct)).ListOutboxAsync(status, normalizedSkip, normalizedTake, ct);
        return ToDictionaries(rows);
    }

    public Task<IReadOnlyList<string>> GetTestUseCasesAsync(CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var keys = UseCaseProfiles.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public Task EnsureHostInitializedAsync(CancellationToken ct)
        => EnsureInitializedAsync(ct);

    public async Task<IReadOnlyList<Dictionary<string, object?>>> CreateTestEntitiesAsync(string useCase, int count, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(useCase)) throw new ArgumentException("useCase is required.", nameof(useCase));
        if (count < 1) return Array.Empty<Dictionary<string, object?>>();
        if (count > 1000) throw new ArgumentException("count is too high. max=1000", nameof(count));

        await EnsureInitializedAsync(ct);
        if (_engine == null) throw new InvalidOperationException("Engine runtime is not initialized.");

        if (!UseCaseProfiles.TryGetValue(useCase.Trim(), out var profile))
            throw new ArgumentException($"Unknown use-case '{useCase}'.", nameof(useCase));

        var results = new List<Dictionary<string, object?>>(count);
        for (var i = 0; i < count; i++) {
            ct.ThrowIfCancellationRequested();
            var entityId = Guid.NewGuid().ToString("N");
            var trigger = await _engine.TriggerAsync(new LifeCycleTriggerRequest {
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
        try { await _consumerInitiator.StopAsync(CancellationToken.None); } catch { }
        _initLock.Dispose();
        _consumerInitLock.Dispose();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) {
        if (_runtimeStarted) return;

        await _initLock.WaitAsync(ct);
        try {
            if (_runtimeStarted) return;

            await _engineService.EnsureHostInitializedAsync(ct);
            _engine = await _engineAccessor.GetEngineAsync(ct);

            await ImportUseCasesAsync(ct);

            _consumerInitiator.RegisterAssembly(typeof(ChangeRequestWrapper).Assembly);
            await _consumerInitiator.EnsureHostInitializedAsync(ct);
            _runtimeStarted = true;
        } finally {
            _initLock.Release();
        }
    }

    private async Task ImportUseCasesAsync(CancellationToken ct) {
        if (_engine == null) throw new InvalidOperationException("Engine runtime is not initialized.");

        for (var i = 0; i < UseCaseProfiles.Count; i++) {
            ct.ThrowIfCancellationRequested();
            var profile = UseCaseProfiles.Values.ElementAt(i);

            var definitionPath = ResolveUseCaseFilePath(profile.Folder, profile.DefinitionFile);
            var policyPath = ResolveUseCaseFilePath(profile.Folder, profile.PolicyFile);

            var definitionJson = await File.ReadAllTextAsync(definitionPath, ct);
            var policyJson = await File.ReadAllTextAsync(policyPath, ct);

            var defVersionId = await _engine.ImportDefinitionJsonAsync(_options.EnvCode, _options.EnvDisplayName, definitionJson, ct);
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
