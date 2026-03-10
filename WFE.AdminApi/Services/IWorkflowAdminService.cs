namespace WFE.AdminApi.Services;

public interface IWorkflowAdminService {
    Task EnsureHostInitializedAsync(CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerWorkflowsAsync(int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerInboxAsync(int? status, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerOutboxAsync(int? status, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<string>> GetTestUseCasesAsync(CancellationToken ct);
    Task<IReadOnlyList<Dictionary<string, object?>>> CreateTestEntitiesAsync(string useCase, int count, CancellationToken ct);
}
