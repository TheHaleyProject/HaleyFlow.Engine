using Haley.Enums;
using Microsoft.AspNetCore.Mvc;
using WFE.AdminApi.Services;

namespace WFE.AdminApi.Controllers;

[ApiController]
[Route("api/admin/workflow")]
public sealed class WorkflowAdminController : ControllerBase {
    private readonly IWorkflowAdminService _service;

    public WorkflowAdminController(IWorkflowAdminService service) {
        _service = service;
    }

    [HttpGet("instance")]
    public async Task<IActionResult> GetInstance(
        [FromQuery] int? envCode,
        [FromQuery] string? defName,
        [FromQuery] string? entityId,
        [FromQuery] string? instanceGuid,
        CancellationToken ct) {
        var data = await _service.GetInstanceAsync(envCode, defName, entityId, instanceGuid, ct);
        if (data == null) return NotFound();
        return Ok(data);
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline(
        [FromQuery] int? envCode,
        [FromQuery] string? defName,
        [FromQuery] string? entityId,
        [FromQuery] string? instanceGuid,
        CancellationToken ct) {
        var timelineJson = await _service.GetTimelineJsonAsync(envCode, defName, entityId, instanceGuid, ct);
        if (string.IsNullOrWhiteSpace(timelineJson)) return NotFound();
        return Content(timelineJson, "application/json");
    }

    [HttpGet("refs")]
    public async Task<IActionResult> GetInstanceRefs(
        [FromQuery] int? envCode,
        [FromQuery] string defName,
        [FromQuery] string? flags,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(defName)) return BadRequest("defName is required.");

        var parsedFlags = ParseFlags(flags);
        var refs = await _service.GetInstanceRefsAsync(envCode, defName, parsedFlags, skip, take, ct);
        return Ok(refs);
    }

    [HttpGet("entities")]
    public async Task<IActionResult> GetEngineEntities(
        [FromQuery] string? defName,
        [FromQuery] bool runningOnly = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) {
        var rows = await _service.GetEngineEntitiesAsync(defName, runningOnly, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("pending-acks")]
    public async Task<IActionResult> GetPendingAcks(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) {
        var rows = await _service.GetPendingAcksAsync(skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("consumer/workflows")]
    public async Task<IActionResult> GetConsumerWorkflows(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) {
        var rows = await _service.GetConsumerWorkflowsAsync(skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("consumer/inbox")]
    public async Task<IActionResult> GetConsumerInbox(
        [FromQuery] int? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) {
        var rows = await _service.GetConsumerInboxAsync(status, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("consumer/outbox")]
    public async Task<IActionResult> GetConsumerOutbox(
        [FromQuery] int? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default) {
        var rows = await _service.GetConsumerOutboxAsync(status, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct) {
        var summary = await _service.GetSummaryAsync(ct);
        return Ok(summary);
    }

    private static LifeCycleInstanceFlag ParseFlags(string? flags) {
        if (string.IsNullOrWhiteSpace(flags)) return LifeCycleInstanceFlag.Active;

        var parsed = LifeCycleInstanceFlag.None;
        var parts = flags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++) {
            if (Enum.TryParse<LifeCycleInstanceFlag>(parts[i], true, out var value)) {
                parsed |= value;
            }
        }

        return parsed == LifeCycleInstanceFlag.None ? LifeCycleInstanceFlag.Active : parsed;
    }
}
