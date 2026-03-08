namespace WFE.Test.UseCases.ChangeRequest {
    internal sealed class ChangeRequestUseCase : IWorkflowUseCase {
        public string Name => "change-request";
        public string Description => SharedUseCaseHost.GetDescriptionOrDefault(Name);

        public Task RunAsync(CancellationToken ct)
            => SharedUseCaseHost.RunAsync(Name, ct);
    }
}
