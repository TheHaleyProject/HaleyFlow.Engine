namespace WFE.Test.UseCases.PaperlessReview {
    internal sealed class PaperlessReviewUseCase : IWorkflowUseCase {
        public string Name => "paperless-review";
        public string Description => SharedUseCaseHost.GetDescriptionOrDefault(Name);

        public Task RunAsync(CancellationToken ct)
            => SharedUseCaseHost.RunAsync(Name, ct);
    }
}
