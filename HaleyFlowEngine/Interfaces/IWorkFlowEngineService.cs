using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions;

public interface IWorkFlowEngineService : IWorkFlowEngineAccessor {
    Task<LifeCycleInstanceData?> GetInstanceAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, CancellationToken ct);
    Task<string?> GetTimelineJsonAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, TimelineDetail detail, CancellationToken ct);
    Task<string?> GetTimelineHtmlAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, string? displayName, TimelineDetail detail, HtmlTimelineDesign design, string? color, CancellationToken ct);
    Task<IReadOnlyList<InstanceRefItem>> GetInstanceRefsAsync(int? envCode, string defName, LifeCycleInstanceFlag flags, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetEngineEntitiesAsync(int envCode, string? defName, bool runningOnly, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetEngineInstancesByStatusAsync(int envCode, string? defName, LifeCycleInstanceFlag statusFlags, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetPendingAcksAsync(int envCode, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetRecentNoticesAsync(string? code, string? kind, string? instanceGuid, string? ackGuid, int skip, int take, CancellationToken ct);
    Task<Dictionary<string, object?>> GetSummaryAsync(int envCode, CancellationToken ct);
    Task<Dictionary<string, object?>> GetHealthAsync(CancellationToken ct);
    Task<Dictionary<string, object?>> EnsureHostInitializedAsync(CancellationToken ct);
    Task<bool> SuspendInstanceAsync(string instanceGuid, string? message, CancellationToken ct);
    Task<bool> ResumeInstanceAsync(string instanceGuid, CancellationToken ct);
    Task<bool> FailInstanceAsync(string instanceGuid, string? message, CancellationToken ct);
    Task<LifeCycleTriggerResult> ReopenInstanceAsync(string instanceGuid, string actor, CancellationToken ct);
    Task<bool> UnsuspendInstanceAsync(string instanceGuid, string actor, CancellationToken ct);

    // ── Backfill ─────────────────────────────────────────────────────────────────────────────
    Task<WorkflowDefinitionSnapshot?> GetDefinitionSnapshotAsync(int envCode, string definitionName, CancellationToken ct);
    Task<BackfillImportResult> ImportBackfillAsync(WorkflowBackfillObject obj, CancellationToken ct);
}

