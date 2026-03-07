namespace WFE.Test.UseCases {
    internal interface IWorkflowUseCase {
        string Name { get; }
        string Description { get; }
        Task RunAsync(CancellationToken ct);
    }
}
