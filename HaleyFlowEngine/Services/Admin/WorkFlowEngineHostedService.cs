using Haley.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Haley.Services;

internal sealed class WorkFlowEngineHostedService : IHostedService {
    private readonly IWorkFlowEngineService _engineService;

    public WorkFlowEngineHostedService(IWorkFlowEngineService engineService) {
        _engineService = engineService ?? throw new ArgumentNullException(nameof(engineService));
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => _engineService.EnsureHostInitializedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
