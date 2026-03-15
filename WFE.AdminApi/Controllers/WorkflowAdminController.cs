using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WFE.AdminApi.Configuration;
using WFE.AdminApi.Services;
using Haley.Utils;

namespace WFE.AdminApi.Controllers;

[ApiController]
[Route("api/admin/wf/engine")]
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

    [HttpGet("/api/admin/wf/consumer/workflows")]
    public async Task<IActionResult> GetConsumerWorkflows([FromQuery] ConsumerWorkflowFilter? filter, CancellationToken ct = default) {
        await _testBootstrap.EnsureInitializedAsync(ct);
        var rows = await _consumerService.ListWorkflowsAsync(NormalizePaging(filter), ct);
        return Ok(rows.ToConsumerWorkflowDictionaries());
    }

    [HttpGet("/api/admin/wf/consumer/inbox")]
    public async Task<IActionResult> GetConsumerInbox([FromQuery] ConsumerInboxFilter? filter, CancellationToken ct = default) {
        await _testBootstrap.EnsureInitializedAsync(ct);
        var rows = await _consumerService.ListInboxAsync(NormalizePaging(filter), ct);
        return Ok(rows.ToInboxItemDictionaries());
    }

    [HttpGet("/api/admin/wf/consumer/inbox-status")]
    public async Task<IActionResult> GetConsumerInboxStatus([FromQuery] ConsumerInboxStatusFilter? filter, CancellationToken ct = default) {
        await _testBootstrap.EnsureInitializedAsync(ct);
        var rows = await _consumerService.ListInboxStatusesAsync(NormalizePaging(filter), ct);
        return Ok(rows.ToInboxStatusDictionaries());
    }

    [HttpGet("/api/admin/wf/consumer/outbox")]
    public async Task<IActionResult> GetConsumerOutbox([FromQuery] ConsumerOutboxFilter? filter, CancellationToken ct = default) {
        await _testBootstrap.EnsureInitializedAsync(ct);
        var rows = await _consumerService.ListOutboxAsync(NormalizePaging(filter), ct);
        return Ok(rows.ToOutboxDictionaries());
    }

    [HttpGet("/api/admin/wf/consumer/timeline/{instanceGuid}")]
    public async Task<IActionResult> GetConsumerTimeline(string instanceGuid, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) return BadRequest("instanceGuid is required.");
        await _testBootstrap.EnsureInitializedAsync(ct);
        var timeline = await _consumerService.GetConsumerTimelineAsync(instanceGuid.Trim(), ct);
        return Ok(timeline);
    }

    [HttpGet("/api/admin/wf/test/usecases")]
    public async Task<IActionResult> GetTestUseCases(CancellationToken ct) {
        var useCases = await _testBootstrap.GetTestUseCasesAsync(ct);
        return Ok(useCases);
    }

    [HttpPost("/api/admin/wf/test/entities")]
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

    private ConsumerWorkflowFilter NormalizePaging(ConsumerWorkflowFilter? filter) {
        filter ??= new ConsumerWorkflowFilter();
        var (skip, take) = NormalizePaging(filter.Skip, filter.Take);
        filter.Skip = skip;
        filter.Take = take;
        return filter;
    }

    private ConsumerInboxFilter NormalizePaging(ConsumerInboxFilter? filter) {
        filter ??= new ConsumerInboxFilter();
        var (skip, take) = NormalizePaging(filter.Skip, filter.Take);
        filter.Skip = skip;
        filter.Take = take;
        return filter;
    }

    private ConsumerInboxStatusFilter NormalizePaging(ConsumerInboxStatusFilter? filter) {
        filter ??= new ConsumerInboxStatusFilter();
        var (skip, take) = NormalizePaging(filter.Skip, filter.Take);
        filter.Skip = skip;
        filter.Take = take;
        return filter;
    }

    private ConsumerOutboxFilter NormalizePaging(ConsumerOutboxFilter? filter) {
        filter ??= new ConsumerOutboxFilter();
        var (skip, take) = NormalizePaging(filter.Skip, filter.Take);
        filter.Skip = skip;
        filter.Take = take;
        return filter;
    }

    public sealed class CreateTestEntitiesRequest {
        public string UseCase { get; set; } = "change-request";
        public int Count { get; set; } = 1;
    }
}
