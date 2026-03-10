using Haley.Enums;
using Microsoft.AspNetCore.Mvc;
using Haley.Services;
using Haley.Abstractions;
using Haley.Services;

namespace Haley.Models;
public class WorkflowAdminController : ControllerBase {
    private readonly IWFEngineAdminService _service;

    public WorkflowAdminController(IWFEngineAdminService service) {
        _service = service;
    }

    [HttpGet("instance")]
    public async Task<IActionResult> GetInstance([FromQuery] int? envCode, [FromQuery] string? defName, [FromQuery] string? entityId, [FromQuery] string? instanceGuid, CancellationToken ct) {
        var data = await _service.GetInstanceAsync(envCode, defName, entityId, instanceGuid, ct);
        if (data == null) return NotFound();
        return Ok(data);
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline([FromQuery] int? envCode, [FromQuery] string? defName, [FromQuery] string? entityId, [FromQuery] string? instanceGuid, CancellationToken ct) {
        var timelineJson = await _service.GetTimelineJsonAsync(envCode, defName, entityId, instanceGuid, ct);
        if (string.IsNullOrWhiteSpace(timelineJson)) return NotFound();
        return Content(timelineJson, "application/json");
    }

    [HttpGet("timeline/html")]
    public async Task<IActionResult> GetTimelineHtml([FromQuery] int? envCode, [FromQuery] string? defName, [FromQuery] string? entityId, [FromQuery] string? instanceGuid, [FromQuery] string? name, CancellationToken ct) {
        var html = await _service.GetTimelineHtmlAsync(envCode, defName, entityId, instanceGuid, name, ct);
        if (string.IsNullOrWhiteSpace(html)) return NotFound();
        return Content(html, "text/html");
    }

    [HttpGet("refs")]
    public async Task<IActionResult> GetInstanceRefs([FromQuery] int? envCode, [FromQuery] string defName, [FromQuery] string? flags, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(defName)) return BadRequest("defName is required.");

        var parsedFlags = ParseFlags(flags);
        var refs = await _service.GetInstanceRefsAsync(envCode, defName, parsedFlags, skip, take, ct);
        return Ok(refs);
    }

    [HttpGet("entities")]
    public async Task<IActionResult> GetEngineEntities([FromQuery] string? defName, [FromQuery] bool runningOnly = false, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        var rows = await _service.GetEngineEntitiesAsync(defName, runningOnly, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("pending-acks")]
    public async Task<IActionResult> GetPendingAcks([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        var rows = await _service.GetPendingAcksAsync(skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct) {
        var summary = await _service.GetSummaryAsync(ct);
        return Ok(summary);
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct) {
        var health = await _service.GetHealthAsync(ct);
        var isHealthy = health.TryGetValue("status", out var s) && s is string str && str == "healthy";
        return StatusCode(isHealthy ? 200 : 503, health);
    }

    [HttpPost("runtime/ensure-started")]
    public async Task<IActionResult> EnsureRuntimeStarted(CancellationToken ct) {
        var result = await _service.EnsureHostInitializedAsync(ct);
        return Ok(result);
    }

    [HttpPost("instance/reopen")]
    public async Task<IActionResult> ReopenInstance([FromBody] ReopenInstanceRequest request, CancellationToken ct) {
        if (request == null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.InstanceGuid)) return BadRequest("instanceGuid is required.");

        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "wfe.adminapi.reopen" : request.Actor.Trim();
        var result = await _service.ReopenInstanceAsync(request.InstanceGuid.Trim(), actor, ct);
        return Ok(result);
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