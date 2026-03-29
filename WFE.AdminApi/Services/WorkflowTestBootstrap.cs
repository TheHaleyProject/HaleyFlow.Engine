using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using WFE.Test.UseCases.ChangeRequest;
using WFE.Test.UseCases.LoanApproval;
using WFE.Test.UseCases.PaperlessReview;
using WFE.Test.UseCases.VendorRegistration;

namespace WFE.AdminApi.Services;

public sealed class WorkflowTestBootstrap {
    private sealed record UseCaseProfile(string Key, string DefName, string StartEvent);

    private static readonly IReadOnlyDictionary<string, UseCaseProfile> UseCaseProfiles =
        new Dictionary<string, UseCaseProfile>(StringComparer.OrdinalIgnoreCase) {
            ["change-request"]      = new("change-request",      ChangeRequestWrapper.DefinitionNameConst,      "4000"),
            ["loan-approval"]       = new("loan-approval",       LoanApprovalWrapper.DefinitionNameConst,       "2000"),
            ["paperless-review"]    = new("paperless-review",    PaperlessReviewWrapper.DefinitionNameConst,    "3000"),
            ["vendor-registration"] = new("vendor-registration", VendorRegistrationWrapper.DefinitionNameConst, "1000"),
        };

    private readonly IFlowBus _flowBus;

    public WorkflowTestBootstrap(IFlowBus flowBus) {
        _flowBus = flowBus ?? throw new ArgumentNullException(nameof(flowBus));
    }

    public Task<IReadOnlyList<string>> GetTestUseCasesAsync(CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var keys = UseCaseProfiles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> CreateTestEntitiesAsync(string useCase, int count,  CancellationToken ct,FlowBusMode? mode = null) {
        if (string.IsNullOrWhiteSpace(useCase)) throw new ArgumentException("useCase is required.", nameof(useCase));
        if (count < 1) return Array.Empty<Dictionary<string, object?>>();
        if (count > 1000) throw new ArgumentException("count is too high. max=1000", nameof(count));

        if (!UseCaseProfiles.TryGetValue(useCase.Trim(), out var profile))
            throw new ArgumentException($"Unknown use-case '{useCase}'.", nameof(useCase));

        var results = new List<Dictionary<string, object?>>(count);
        for (var i = 0; i < count; i++) {
            ct.ThrowIfCancellationRequested();
            var entityGuid = Guid.NewGuid().ToString();
            var responseDic = new Dictionary<string, object?> {
                ["useCase"] = profile.Key,
                ["entityGuid"] = entityGuid
            };
            try {
                var feedback = await _flowBus.InitiateAsync(new FlowInitiateRequest {
                    WorkflowName = profile.DefName,
                    EntityId = entityGuid,
                    StartEvent = profile.StartEvent,
                    Actor = "wfe.adminapi.test",
                    Mode = mode,
                    Payload = new Dictionary<string, object> {
                        ["source"] = "WFE.AdminApi",
                        ["useCase"] = profile.Key,
                        ["requestIndex"] = i + 1
                    }
                }, ct);
                responseDic["success"] = feedback.Status;
                responseDic["message"] = feedback.Message;
                responseDic["data"] = feedback.Result;
            } catch (Exception ex) {
                responseDic["success"] = false;
                responseDic["message"] = $"Exception: {ex.GetType().FullName} - {ex.Message}";
            }

            results.Add(responseDic);
        }

        return results;
    }
}
