using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions;

public interface IWorkFlowEngineService {
    Task<LifeCycleInstanceData?> GetInstanceAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, CancellationToken ct);
    Task<string?> GetTimelineJsonAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, CancellationToken ct);
    Task<string?> GetTimelineHtmlAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, string? displayName, CancellationToken ct);
    Task<IReadOnlyList<InstanceRefItem>> GetInstanceRefsAsync(int? envCode, string defName, LifeCycleInstanceFlag flags, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetEngineEntitiesAsync(string? defName, bool runningOnly, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetPendingAcksAsync(int skip, int take, CancellationToken ct);
    Task<Dictionary<string, object?>> GetSummaryAsync(CancellationToken ct);
    Task<Dictionary<string, object?>> GetHealthAsync(CancellationToken ct);
    Task<Dictionary<string, object?>> EnsureHostInitializedAsync(CancellationToken ct);
    Task<LifeCycleTriggerResult> ReopenInstanceAsync(string instanceGuid, string actor, CancellationToken ct);
}
