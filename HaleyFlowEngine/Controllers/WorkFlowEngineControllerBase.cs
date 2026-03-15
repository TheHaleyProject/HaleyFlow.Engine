using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Microsoft.AspNetCore.Mvc;

namespace Haley.Models;
public abstract class WorkFlowEngineControllerBase : ControllerBase {
    private readonly IWorkFlowEngineService _service;

    public WorkFlowEngineControllerBase(IWorkFlowEngineService service) {
        _service = service;
    }

    [HttpGet("instance")]
    public async Task<IActionResult> GetInstance([FromQuery] int? envCode, [FromQuery] string? defName, [FromQuery] string? entityId, [FromQuery] string? instanceGuid, CancellationToken ct) {
        var data = await _service.GetInstanceAsync(envCode, defName, entityId, instanceGuid, ct);
        if (data == null) return NotFound();
        return Ok(data);
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline([FromQuery] int? envCode, [FromQuery] string? defName, [FromQuery] string? entityId, [FromQuery] string? instanceGuid, [FromQuery] TimelineDetail detail = TimelineDetail.Detailed, CancellationToken ct = default) {
        var timelineJson = await _service.GetTimelineJsonAsync(envCode, defName, entityId, instanceGuid, detail, ct);
        if (string.IsNullOrWhiteSpace(timelineJson)) return NotFound();
        return Content(timelineJson, "application/json");
    }

    [HttpGet("timeline/html")]
    public async Task<IActionResult> GetTimelineHtml([FromQuery] int? envCode, [FromQuery] string? defName, [FromQuery] string? entityId, [FromQuery] string? instanceGuid, [FromQuery] string? name, [FromQuery] TimelineDetail detail = TimelineDetail.Detailed, [FromQuery] HtmlTimelineDesign design = HtmlTimelineDesign.LightGlass, [FromQuery] string? color = null, CancellationToken ct = default) {
        var html = await _service.GetTimelineHtmlAsync(envCode, defName, entityId, instanceGuid, name, detail, design, color, ct);
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
    public async Task<IActionResult> GetEngineEntities([FromQuery] int envCode, [FromQuery] string? defName, [FromQuery] bool runningOnly = false, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        var rows = await _service.GetEngineEntitiesAsync(envCode, defName, runningOnly, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("instances")]
    public async Task<IActionResult> GetEngineInstances([FromQuery] int envCode, [FromQuery] string? defName, [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        if (!TryParseStatusFlags(status, out var statusFlags))
            return BadRequest("Invalid status filter. Use comma-separated values from: Active, Suspended, Completed, Failed, Archived, or 'all'.");

        var rows = await _service.GetEngineInstancesByStatusAsync(envCode, defName, statusFlags, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("pending-acks")]
    public async Task<IActionResult> GetPendingAcks([FromQuery] int envCode, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        var rows = await _service.GetPendingAcksAsync(envCode, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int envCode, CancellationToken ct) {
        var summary = await _service.GetSummaryAsync(envCode, ct);
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

    [HttpPost("instance/suspend")]
    public async Task<IActionResult> SuspendInstance([FromQuery] string? instanceGuid, [FromQuery] string? message, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) return BadRequest("instanceGuid is required.");

        var normalizedGuid = instanceGuid.Trim();
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        try {
            var suspended = await _service.SuspendInstanceAsync(normalizedGuid, normalizedMessage, ct);
            if (!suspended) return NotFound($"Instance not found for guid '{normalizedGuid}'.");

            return Ok(new {
                status = "ok",
                instanceGuid = normalizedGuid,
                suspended = true
            });
        } catch (InvalidOperationException ex) {
            return Conflict(new {
                status = "failed",
                instanceGuid = normalizedGuid,
                message = ex.Message
            });
        }
    }

    [HttpPost("instance/resume")]
    public async Task<IActionResult> ResumeInstance([FromQuery] string? instanceGuid, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) return BadRequest("instanceGuid is required.");

        var normalizedGuid = instanceGuid.Trim();

        try {
            var resumed = await _service.ResumeInstanceAsync(normalizedGuid, ct);
            if (!resumed) return NotFound($"Instance not found for guid '{normalizedGuid}'.");

            return Ok(new {
                status = "ok",
                instanceGuid = normalizedGuid,
                resumed = true
            });
        } catch (InvalidOperationException ex) {
            return Conflict(new {
                status = "failed",
                instanceGuid = normalizedGuid,
                message = ex.Message
            });
        }
    }

    [HttpPost("instance/fail")]
    public async Task<IActionResult> FailInstance([FromQuery] string? instanceGuid, [FromQuery] string? message, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) return BadRequest("instanceGuid is required.");

        var normalizedGuid = instanceGuid.Trim();
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        try {
            var marked = await _service.FailInstanceAsync(normalizedGuid, normalizedMessage, ct);
            if (!marked) return NotFound($"Instance not found for guid '{normalizedGuid}'.");

            return Ok(new {
                status = "ok",
                instanceGuid = normalizedGuid,
                failed = true
            });
        } catch (InvalidOperationException ex) {
            return Conflict(new {
                status = "failed",
                instanceGuid = normalizedGuid,
                message = ex.Message
            });
        }
    }

    [HttpPost("instance/reopen")]
    public async Task<IActionResult> ReopenInstance([FromQuery] string? instanceGuid, [FromQuery] string? actor, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) return BadRequest("instanceGuid is required.");

        var normalizedGuid = instanceGuid.Trim();
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "wfe.adminapi.reopen" : actor.Trim();
        var result = await _service.ReopenInstanceAsync(normalizedGuid, normalizedActor, ct);

        if (!result.Applied && string.Equals(result.Reason, "NotTerminal", StringComparison.OrdinalIgnoreCase)) {
            return Conflict(new {
                status = "failed",
                instanceGuid = normalizedGuid,
                message = "Reopen is allowed only for terminal instances (Failed, Archived, Completed)."
            });
        }

        return Ok(result);
    }

    // Extends the ACK retry budget for all Failed ack_consumer rows on the instance and clears
    // the Suspended flag.  Use this when suspension was caused by exhausted ack max-retries.
    // trigger_count is preserved (monotonically increasing audit counter).
    [HttpPost("instance/unsuspend")]
    public async Task<IActionResult> UnsuspendInstance([FromQuery] string? instanceGuid, [FromQuery] string? actor, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) return BadRequest("instanceGuid is required.");

        var normalizedGuid = instanceGuid.Trim();
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "wfe.adminapi.unsuspend" : actor.Trim();
        var ok = await _service.UnsuspendInstanceAsync(normalizedGuid, normalizedActor, ct);

        if (!ok) return NotFound(new { status = "not_found", instanceGuid = normalizedGuid });
        return Ok(new { status = "unsuspended", instanceGuid = normalizedGuid });
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

    private static bool TryParseStatusFlags(string? status, out LifeCycleInstanceFlag flags) {
        flags = LifeCycleInstanceFlag.None;
        if (string.IsNullOrWhiteSpace(status)) return true;

        var parts = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return true;

        for (var i = 0; i < parts.Length; i++) {
            var part = parts[i];
            if (part.Equals("all", StringComparison.OrdinalIgnoreCase)) continue;

            if (!Enum.TryParse<LifeCycleInstanceFlag>(part, true, out var value) || value == LifeCycleInstanceFlag.None)
                return false;

            flags |= value;
        }

        return true;
    }
}
