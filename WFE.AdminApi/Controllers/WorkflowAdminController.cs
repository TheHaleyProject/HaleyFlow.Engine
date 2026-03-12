using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WFE.AdminApi.Configuration;
using WFE.AdminApi.Services;

namespace WFE.AdminApi.Controllers;

[ApiController]
[Route("api/admin/workflow")]
public sealed class WorkflowAdminController : WorkFlowEngineControllerBase {
    private readonly WorkflowAdminOptions _adminOptions;
    private readonly IWorkFlowConsumerService _consumerService;
    private readonly WorkflowTestBootstrap _testBootstrap;

    public WorkflowAdminController(
        IWorkFlowEngineService engineService,
        IOptions<WorkflowAdminOptions> adminOptions,
        WorkflowTestBootstrap testBootstrap,
        IWorkFlowConsumerService consumerService) : base(engineService) {
        _adminOptions = adminOptions?.Value ?? throw new ArgumentNullException(nameof(adminOptions));
        _consumerService = consumerService ?? throw new ArgumentNullException(nameof(consumerService));
        _testBootstrap = testBootstrap ?? throw new ArgumentNullException(nameof(testBootstrap));
    }

    [HttpGet("consumer/workflows")]
    public async Task<IActionResult> GetConsumerWorkflows([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        await _testBootstrap.EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await _consumerService.ListWorkflowsAsync(normalizedSkip, normalizedTake, ct);
        return Ok(ToDictionaries(rows));
    }

    [HttpGet("consumer/inbox")]
    public async Task<IActionResult> GetConsumerInbox(
        [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        await _testBootstrap.EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await _consumerService.ListInboxAsync(status, normalizedSkip, normalizedTake, ct);
        return Ok(ToDictionaries(rows));
    }

    [HttpGet("consumer/outbox")]
    public async Task<IActionResult> GetConsumerOutbox([FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        await _testBootstrap.EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await _consumerService.ListOutboxAsync(status, normalizedSkip, normalizedTake, ct);
        return Ok(ToDictionaries(rows));
    }

    [HttpGet("test/usecases")]
    public async Task<IActionResult> GetTestUseCases(CancellationToken ct) {
        var useCases = await _testBootstrap.GetTestUseCasesAsync(ct);
        return Ok(useCases);
    }

    [HttpPost("test/entities")]
    public async Task<IActionResult> CreateTestEntities(
        [FromBody] CreateTestEntitiesRequest request,
        CancellationToken ct) {
        if (request == null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.UseCase)) return BadRequest("useCase is required.");
        if (request.Count < 1) return BadRequest("count must be greater than 0.");

        var results = await _testBootstrap.CreateTestEntitiesAsync(request.UseCase, request.Count, ct);
        return Ok(new {
            useCase = request.UseCase,
            requested = request.Count,
            created = results.Count,
            results
        });
    }

    private (int skip, int take) NormalizePaging(int skip, int take) {
        var normalizedSkip = skip < 0 ? 0 : skip;
        var fallbackTake = _adminOptions.DefaultTake > 0 ? _adminOptions.DefaultTake : 50;
        var normalizedTake = take <= 0 ? fallbackTake : take;
        var maxTake = _adminOptions.MaxTake > 0 ? _adminOptions.MaxTake : 500;
        if (normalizedTake > maxTake) normalizedTake = maxTake;
        return (normalizedSkip, normalizedTake);
    }

    private static IReadOnlyList<Dictionary<string, object?>> ToDictionaries(DbRows rows) {
        var result = new List<Dictionary<string, object?>>(rows.Count);
        for (var i = 0; i < rows.Count; i++) {
            var row = rows[i];
            var item = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in row) {
                item[entry.Key] = entry.Value == DBNull.Value ? null : entry.Value;
            }
            result.Add(item);
        }

        return result;
    }

    public sealed class CreateTestEntitiesRequest {
        public string UseCase { get; set; } = "change-request";
        public int Count { get; set; } = 1;
    }
}
