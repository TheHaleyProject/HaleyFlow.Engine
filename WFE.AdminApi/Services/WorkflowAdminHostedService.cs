using Microsoft.Extensions.Hosting;

namespace WFE.AdminApi.Services;

internal sealed class WorkflowAdminHostedService : IHostedService {
    private readonly IWorkflowAdminService _adminService;

    public WorkflowAdminHostedService(IWorkflowAdminService adminService) {
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => _adminService.EnsureHostInitializedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
