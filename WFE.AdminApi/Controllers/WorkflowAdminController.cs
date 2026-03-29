using Microsoft.AspNetCore.Mvc;
using WFE.AdminApi.Services;

namespace WFE.AdminApi.Controllers;

[ApiController]
public sealed class WorkflowAdminController : ControllerBase {
    private readonly WorkflowTestBootstrap _testBootstrap;

    public WorkflowAdminController(WorkflowTestBootstrap testBootstrap) {
        _testBootstrap = testBootstrap ?? throw new ArgumentNullException(nameof(testBootstrap));
    }

    [HttpGet("/api/admin/wf/test/usecases")]
    public async Task<IActionResult> GetTestUseCases(CancellationToken ct) {
        var useCases = await _testBootstrap.GetTestUseCasesAsync(ct);
        return Ok(useCases);
    }

    [HttpPost("/api/admin/wf/test/entities")]
    public async Task<IActionResult> CreateTestEntities([FromBody] CreateTestEntitiesRequest request, CancellationToken ct) {
        if (request == null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.UseCase)) return BadRequest("useCase is required.");
        if (request.Count < 1) return BadRequest("count must be greater than 0.");

        var results = await _testBootstrap.CreateTestEntitiesAsync(request.UseCase, request.Count, ct);
        return Ok(new {
            useCase   = request.UseCase,
            requested = request.Count,
            created   = results.Count,
            results
        });
    }

    public sealed class CreateTestEntitiesRequest {
        public string UseCase { get; set; } = "loan-approval";
        public int Count { get; set; } = 1;
    }
}
