using Haley.Enums;
using Haley.Models;

namespace WFE.AdminApi.Services;

public interface IWorkflowAdminService {
    Task<LifeCycleInstanceData?> GetInstanceAsync(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid,
        CancellationToken ct);

    Task<string?> GetTimelineJsonAsync(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid,
        CancellationToken ct);

    Task<string?> GetTimelineHtmlAsync(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid,
        string? displayName,
        CancellationToken ct);

    Task<IReadOnlyList<InstanceRefItem>> GetInstanceRefsAsync(
        int? envCode,
        string defName,
        LifeCycleInstanceFlag flags,
        int skip,
        int take,
        CancellationToken ct);

    Task<IReadOnlyList<Dictionary<string, object?>>> GetEngineEntitiesAsync(
        string? defName,
        bool runningOnly,
        int skip,
        int take,
        CancellationToken ct);

    Task<IReadOnlyList<Dictionary<string, object?>>> GetPendingAcksAsync(
        int skip,
        int take,
        CancellationToken ct);

    Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerWorkflowsAsync(
        int skip,
        int take,
        CancellationToken ct);

    Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerInboxAsync(
        int? status,
        int skip,
        int take,
        CancellationToken ct);

    Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerOutboxAsync(
        int? status,
        int skip,
        int take,
        CancellationToken ct);

    Task<Dictionary<string, object?>> GetSummaryAsync(CancellationToken ct);

    /// <summary>
    /// Pings both the engine and consumer databases.
    /// Returns status "healthy" when all checks pass, "unhealthy" when any fail.
    /// </summary>
    Task<Dictionary<string, object?>> GetHealthAsync(CancellationToken ct);

    /// <summary>
    /// Ensures engine host/runtime is initialized and running.
    /// Does not create any test entities or fire workflow triggers.
    /// </summary>
    Task<Dictionary<string, object?>> EnsureHostInitializedAsync(CancellationToken ct);

    Task<IReadOnlyList<string>> GetTestUseCasesAsync(CancellationToken ct);

    Task<LifeCycleTriggerResult> ReopenInstanceAsync(
        string instanceGuid,
        string actor,
        CancellationToken ct);

    Task<IReadOnlyList<Dictionary<string, object?>>> CreateTestEntitiesAsync(
        string useCase,
        int count,
        CancellationToken ct);
}
