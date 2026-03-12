using Haley.Abstractions;
using Haley.Models;
using Microsoft.Extensions.Options;
using WFE.AdminApi.Configuration;
using WFE.Test.UseCases.ChangeRequest;
using WFE.Test.UseCases.LoanApproval;
using WFE.Test.UseCases.PaperlessReview;
using WFE.Test.UseCases.VendorRegistration;

namespace WFE.AdminApi.Services;

public sealed class WorkflowTestBootstrap {
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
                ChangeRequestWrapper.DefinitionNameConst,
                "4000",
                "ChangeRequest",
                "definition.change_request.json",
                "policy.change_request.json"),
            ["loan-approval"] = new(
                "loan-approval",
                LoanApprovalWrapper.DefinitionNameConst,
                "2000",
                "LoanApproval",
                "definition.loan_approval.json",
                "policy.loan_approval.json"),
            ["paperless-review"] = new(
                "paperless-review",
                PaperlessReviewWrapper.DefinitionNameConst,
                "3000",
                "PaperlessReview",
                "definition.paperless_review.json",
                "policy.paperless_review.json"),
            ["vendor-registration"] = new(
                "vendor-registration",
                VendorRegistrationWrapper.DefinitionNameConst,
                "1000",
                "VendorRegistration",
                "definition.vendor_registration.json",
                "policy.vendor_registration.json")
        };

    private readonly WorkflowAdminOptions _adminOptions;
    private readonly ConsumerServiceOptions _consumerOptions;
    private readonly IWorkFlowEngineService _engineService;
    private readonly IWorkFlowConsumerService _consumerService;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IWorkFlowEngine? _engine;
    private bool _initialized;

    public WorkflowTestBootstrap(
        IOptions<WorkflowAdminOptions> adminOptions,
        IOptions<ConsumerServiceOptions> consumerOptions,
        IWorkFlowEngineService engineService,
        IWorkFlowConsumerService consumerService) {
        _adminOptions = adminOptions?.Value ?? throw new ArgumentNullException(nameof(adminOptions));
        _consumerOptions = consumerOptions?.Value ?? throw new ArgumentNullException(nameof(consumerOptions));
        _engineService = engineService ?? throw new ArgumentNullException(nameof(engineService));
        _consumerService = consumerService ?? throw new ArgumentNullException(nameof(consumerService));
    }

    public async Task EnsureInitializedAsync(CancellationToken ct) {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try {
            if (_initialized) return;

            // Wrapper registration is consumer-side. Assemblies are resolved by WorkFlowConsumerService
            // from ConsumerBootstrapOptions.WrapperAssemblies before the consumer runtime starts.
            await _engineService.EnsureHostInitializedAsync(ct);
            _engine = await _engineService.GetEngineAsync(ct);

            await ImportUseCasesAsync(ct);
            await _consumerService.EnsureHostInitializedAsync(ct);
            _initialized = true;
        } finally {
            _initLock.Release();
        }
    }

    public Task<IReadOnlyList<string>> GetTestUseCasesAsync(CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var keys = UseCaseProfiles.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

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
                EnvCode = _consumerOptions.EnvCode,
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

    private async Task ImportUseCasesAsync(CancellationToken ct) {
        if (_engine == null) throw new InvalidOperationException("Engine runtime is not initialized.");

        for (var i = 0; i < UseCaseProfiles.Count; i++) {
            ct.ThrowIfCancellationRequested();
            var profile = UseCaseProfiles.Values.ElementAt(i);

            var definitionPath = ResolveUseCaseFilePath(profile.Folder, profile.DefinitionFile);
            var policyPath = ResolveUseCaseFilePath(profile.Folder, profile.PolicyFile);

            var definitionJson = await File.ReadAllTextAsync(definitionPath, ct);
            var policyJson = await File.ReadAllTextAsync(policyPath, ct);

            var defVersionId = await _engine.ImportDefinitionJsonAsync(_consumerOptions.EnvCode, _consumerOptions.EnvDisplayName, definitionJson, ct);
            var policyId = await _engine.ImportPolicyJsonAsync(_consumerOptions.EnvCode, _consumerOptions.EnvDisplayName, policyJson, ct);
            await _engine.InvalidateAsync(_consumerOptions.EnvCode, profile.DefName, ct);

            Console.WriteLine($"[TEST-RUNTIME] Imported use-case={profile.Key} defVersionId={defVersionId} policyId={policyId}");
        }
    }

    private string ResolveUseCaseFilePath(string folder, string fileName) {
        var candidateRoots = new List<string>(6);
        if (!string.IsNullOrWhiteSpace(_adminOptions.UseCasesRootPath)) {
            candidateRoots.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _adminOptions.UseCasesRootPath)));
            candidateRoots.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), _adminOptions.UseCasesRootPath)));
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
}
