namespace WFE.Test.UseCases.VendorRegistration {
    internal sealed class VendorRegistrationUseCase : IWorkflowUseCase {
        public string Name => "vendor-registration";
        public string Description => SharedUseCaseHost.GetDescriptionOrDefault(Name);

        public Task RunAsync(CancellationToken ct)
            => SharedUseCaseHost.RunAsync(Name, ct);
    }
}
