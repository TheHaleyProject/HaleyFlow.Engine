namespace WFE.Test.UseCases.LoanApproval {
    internal sealed class LoanApprovalUseCase : IWorkflowUseCase {
        public string Name => "loan-approval";
        public string Description => SharedUseCaseHost.GetDescriptionOrDefault(Name);

        public Task RunAsync(CancellationToken ct)
            => SharedUseCaseHost.RunAsync(Name, ct);
    }
}
