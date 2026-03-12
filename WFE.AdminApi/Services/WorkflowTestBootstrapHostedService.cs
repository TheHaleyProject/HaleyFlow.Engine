using Microsoft.Extensions.Hosting;

namespace WFE.AdminApi.Services;

internal sealed class WorkflowTestBootstrapHostedService : IHostedService {
    private readonly WorkflowTestBootstrap _bootstrap;

    public WorkflowTestBootstrapHostedService(WorkflowTestBootstrap bootstrap) {
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => _bootstrap.EnsureInitializedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
