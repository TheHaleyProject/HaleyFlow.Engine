using Haley.Abstractions;
using Haley.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WFE.AdminApi.Configuration;

namespace WFE.AdminApi.Services;

internal sealed class WorkflowTestBootstrapHostedService : IHostedService {
    private sealed record UseCaseProfile(string Key, string DefName, string Folder, string DefinitionFile, string PolicyFile);

    private static readonly IReadOnlyList<UseCaseProfile> Profiles = new[] {
        new UseCaseProfile("change-request",     "ProjectChangeRequest", "ChangeRequest",      "definition.change_request.json",     "policy.change_request.json"),
        new UseCaseProfile("loan-approval",      "LoanApproval",         "LoanApproval",       "definition.loan_approval.json",      "policy.loan_approval.json"),
        new UseCaseProfile("paperless-review",   "PaperlessReview",      "PaperlessReview",    "definition.paperless_review.json",   "policy.paperless_review.json"),
        new UseCaseProfile("vendor-registration","VendorRegistration",   "VendorRegistration", "definition.vendor_registration.json","policy.vendor_registration.json"),
    };

    private readonly WorkflowAdminOptions _adminOptions;
    private readonly ConsumerServiceOptions _consumerOptions;
    private readonly IWorkFlowEngineService _engineService;
    private readonly IWorkFlowConsumerService _consumerService;

    public WorkflowTestBootstrapHostedService(IOptions<WorkflowAdminOptions> adminOptions, IOptions<ConsumerServiceOptions> consumerOptions, IWorkFlowEngineService engineService, IWorkFlowConsumerService consumerService) {
        _adminOptions    = adminOptions?.Value    ?? throw new ArgumentNullException(nameof(adminOptions));
        _consumerOptions = consumerOptions?.Value ?? throw new ArgumentNullException(nameof(consumerOptions));
        _engineService   = engineService   ?? throw new ArgumentNullException(nameof(engineService));
        _consumerService = consumerService ?? throw new ArgumentNullException(nameof(consumerService));
    }

    public async Task StartAsync(CancellationToken ct) {
        await _engineService.EnsureHostInitializedAsync(ct);
        var engine = await _engineService.GetEngineAsync(ct);

        foreach (var profile in Profiles) {
            ct.ThrowIfCancellationRequested();

            var definitionPath = ResolveFilePath(profile.Folder, profile.DefinitionFile);
            var policyPath     = ResolveFilePath(profile.Folder, profile.PolicyFile);

            var definitionJson = await File.ReadAllTextAsync(definitionPath, ct);
            var policyJson     = await File.ReadAllTextAsync(policyPath, ct);

            var defVersionId = await engine.ImportDefinitionJsonAsync(_consumerOptions.EnvCode, _consumerOptions.EnvDisplayName, definitionJson, ct);
            var policyId     = await engine.ImportPolicyJsonAsync(_consumerOptions.EnvCode, _consumerOptions.EnvDisplayName, policyJson, ct);
            await engine.InvalidateAsync(_consumerOptions.EnvCode, profile.DefName, ct);

            Console.WriteLine($"[TEST-RUNTIME] Imported use-case={profile.Key} defVersionId={defVersionId} policyId={policyId}");
        }

        await _consumerService.EnsureHostInitializedAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private string ResolveFilePath(string folder, string fileName) {
        var candidateRoots = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(_adminOptions.UseCasesRootPath)) {
            candidateRoots.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _adminOptions.UseCasesRootPath)));
            candidateRoots.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), _adminOptions.UseCasesRootPath)));
        }
        candidateRoots.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WFE.Lib", "UseCases")));
        candidateRoots.Add(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "WFE.Lib", "UseCases")));

        foreach (var root in candidateRoots) {
            var candidate = Path.Combine(root, folder, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Unable to locate '{folder}/{fileName}'. Checked roots: {string.Join(" | ", candidateRoots)}");
    }
}
