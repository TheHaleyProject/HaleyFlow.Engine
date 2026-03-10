using Haley.Enums;
using Haley.Models;
using Microsoft.AspNetCore.Mvc;
using WFE.AdminApi.Services;

namespace WFE.AdminApi.Controllers;

[ApiController]
[Route("api/admin/workflow")]
public sealed class WorkflowAdminController : WorkFlowEngineControllerBase {
    private readonly IWorkflowAdminService _service;

    public WorkflowAdminController(IWorkflowAdminService service) {
        _service = service;
    }

    [HttpGet("consumer/workflows")]
    public async Task<IActionResult> GetConsumerWorkflows([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        var rows = await _service.GetConsumerWorkflowsAsync(skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("consumer/inbox")]
    public async Task<IActionResult> GetConsumerInbox(
        [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        var rows = await _service.GetConsumerInboxAsync(status, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("consumer/outbox")]
    public async Task<IActionResult> GetConsumerOutbox([FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default) {
        var rows = await _service.GetConsumerOutboxAsync(status, skip, take, ct);
        return Ok(rows);
    }

    [HttpGet("test/usecases")]
    public async Task<IActionResult> GetTestUseCases(CancellationToken ct) {
        var useCases = await _service.GetTestUseCasesAsync(ct);
        return Ok(useCases);
    }

    [HttpPost("test/entities")]
    public async Task<IActionResult> CreateTestEntities(
        [FromBody] CreateTestEntitiesRequest request,
        CancellationToken ct) {
        if (request == null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.UseCase)) return BadRequest("useCase is required.");
        if (request.Count < 1) return BadRequest("count must be greater than 0.");

        var results = await _service.CreateTestEntitiesAsync(request.UseCase, request.Count, ct);
        return Ok(new {
            useCase = request.UseCase,
            requested = request.Count,
            created = results.Count,
            results
        });
    }

    public sealed class CreateTestEntitiesRequest {
        public string UseCase { get; set; } = "change-request";
        public int Count { get; set; } = 1;
    }
}
