using Haley.Abstractions;
using Haley.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WFE.AdminApi.Configuration;
using Haley.Utils;

namespace WFE.AdminApi.Controllers;

[ApiController]
[Route("api/admin/wf/engine")]
public sealed class WorkflowEngineAdminController : WorkFlowEngineControllerBase {
    private readonly WorkflowAdminOptions? _adminOptions;
    private readonly IWorkFlowConsumerService? _consumerService;

    public WorkflowEngineAdminController(IWorkFlowEngineService? engineService = null, IOptions<WorkflowAdminOptions>? adminOptions = null, IWorkFlowConsumerService? consumerService = null) : base(engineService!) {
        _adminOptions    = adminOptions?.Value;
        _consumerService = consumerService;
    }

    private IActionResult? EngineUnavailable()
        => _consumerService == null || _adminOptions == null ? NotFound("Engine mode is not enabled.") : null;

    [HttpGet("/api/admin/wf/consumer/instances")]
    public async Task<IActionResult> GetConsumerInstances([FromQuery] ConsumerInstanceFilter? filter, CancellationToken ct = default) {
        if (EngineUnavailable() is { } r) return r;
        var rows = await _consumerService!.ListInstancesAsync(NormalizePaging(filter), ct);
        return Ok(rows.ToConsumerInstanceDictionaries());
    }

    [HttpGet("/api/admin/wf/consumer/inbox")]
    public async Task<IActionResult> GetConsumerInbox([FromQuery] ConsumerInboxFilter? filter, CancellationToken ct = default) {
        if (EngineUnavailable() is { } r) return r;
        var rows = await _consumerService!.ListInboxAsync(NormalizePaging(filter), ct);
        return Ok(rows.ToInboxItemDictionaries());
    }

    [HttpGet("/api/admin/wf/consumer/inbox-status")]
    public async Task<IActionResult> GetConsumerInboxStatus([FromQuery] ConsumerInboxStatusFilter? filter, CancellationToken ct = default) {
        if (EngineUnavailable() is { } r) return r;
        var rows = await _consumerService!.ListInboxStatusesAsync(NormalizePaging(filter), ct);
        return Ok(rows.ToInboxStatusDictionaries());
    }

    [HttpGet("/api/admin/wf/consumer/outbox")]
    public async Task<IActionResult> GetConsumerOutbox([FromQuery] ConsumerOutboxFilter? filter, CancellationToken ct = default) {
        if (EngineUnavailable() is { } r) return r;
        var rows = await _consumerService!.ListOutboxAsync(NormalizePaging(filter), ct);
        return Ok(rows.ToOutboxDictionaries());
    }

    [HttpGet("/api/admin/wf/consumer/timeline/{instanceGuid}")]
    public async Task<IActionResult> GetConsumerTimeline(string instanceGuid, CancellationToken ct = default) {
        if (EngineUnavailable() is { } r) return r;
        if (string.IsNullOrWhiteSpace(instanceGuid)) return BadRequest("instanceGuid is required.");
        var timeline = await _consumerService!.GetConsumerTimelineAsync(instanceGuid.Trim(), ct);
        if (timeline?.Instance == null) return NotFound();
        return Ok(timeline);
    }

    [HttpGet("/api/admin/wf/consumer/timeline/{instanceGuid}/html")]
    public async Task<IActionResult> GetConsumerTimelineHtml(string instanceGuid, [FromQuery] string? name = null, [FromQuery] string? color = null, CancellationToken ct = default) {
        if (EngineUnavailable() is { } r) return r;
        if (string.IsNullOrWhiteSpace(instanceGuid)) return BadRequest("instanceGuid is required.");
        var html = await _consumerService!.GetConsumerTimelineHtmlAsync(instanceGuid.Trim(), name, color, ct);
        if (string.IsNullOrWhiteSpace(html)) return NotFound();
        return Content(html, "text/html");
    }

    private (int skip, int take) NormalizePaging(int skip, int take) {
        var normalizedSkip = skip < 0 ? 0 : skip;
        var fallbackTake   = _adminOptions?.DefaultTake > 0 ? _adminOptions.DefaultTake : 50;
        var normalizedTake = take <= 0 ? fallbackTake : take;
        var maxTake        = _adminOptions?.MaxTake > 0 ? _adminOptions.MaxTake : 500;
        if (normalizedTake > maxTake) normalizedTake = maxTake;
        return (normalizedSkip, normalizedTake);
    }

    private ConsumerInstanceFilter NormalizePaging(ConsumerInstanceFilter? filter) {
        filter ??= new ConsumerInstanceFilter();
        var (skip, take) = NormalizePaging(filter.Skip, filter.Take);
        filter.Skip = skip; filter.Take = take;
        return filter;
    }

    private ConsumerInboxFilter NormalizePaging(ConsumerInboxFilter? filter) {
        filter ??= new ConsumerInboxFilter();
        var (skip, take) = NormalizePaging(filter.Skip, filter.Take);
        filter.Skip = skip; filter.Take = take;
        return filter;
    }

    private ConsumerInboxStatusFilter NormalizePaging(ConsumerInboxStatusFilter? filter) {
        filter ??= new ConsumerInboxStatusFilter();
        var (skip, take) = NormalizePaging(filter.Skip, filter.Take);
        filter.Skip = skip; filter.Take = take;
        return filter;
    }

    private ConsumerOutboxFilter NormalizePaging(ConsumerOutboxFilter? filter) {
        filter ??= new ConsumerOutboxFilter();
        var (skip, take) = NormalizePaging(filter.Skip, filter.Take);
        filter.Skip = skip; filter.Take = take;
        return filter;
    }
}
