using WFE.AdminApi.Configuration;
using WFE.AdminApi.Services;
using Haley.Utils;
using Haley.Models;
using Microsoft.Extensions.Options;
using WFE.Test;
using WFE.Test.UseCases.ChangeRequest;
using WFE.Test.UseCases.LoanApproval;
using WFE.Test.UseCases.PaperlessReview;
using WFE.Test.UseCases.VendorRegistration;

var builder = WebApplication.CreateBuilder(args);
var adminSection = builder.Configuration.GetSection("WorkflowAdmin");

 //── Engine + Consumer mode(comment out this block to run in relay - only mode) ────────────────
builder.Services.Configure<WorkflowAdminOptions>(adminSection);
builder.Services.AddWorkFlowEngineService(autoStart: false, resolveConsumerGuids: async (ty, envCode, defName, cts) => {
    return new List<string> { "89c52807-5054-47fc-9dee-dbb8b42218cb" };
});
builder.Services.AddInProcessEngineProxy();
builder.Services.AddWorkFlowConsumerService(autoStart: false);
builder.Services.AddSingleton(sp => BuildUseCaseRuntimeOptions(
    sp.GetRequiredService<IOptions<WorkflowAdminOptions>>().Value,
    sp.GetRequiredService<IOptions<ConsumerServiceOptions>>().Value));
builder.Services.AddTransient<ChangeRequestWrapper>();
builder.Services.AddTransient<LoanApprovalWrapper>();
builder.Services.AddTransient<VendorRegistrationWrapper>();
builder.Services.AddTransient<PaperlessReviewWrapper>();
builder.Services.AddHostedService<WorkflowTestBootstrapHostedService>(); //since we turned autostart off for both engine and consumer, both wont' start.. we directly start only this one
// ─────────────────────────────────────────────────────────────────────────────────────────────

// ── Relay mode ────────────────────────────────────────────────────────────────────────────────
builder.Services.AddWorkflowRelayService((o) => { o.AssemblyPrefixes.Add("WFE"); });
builder.Services.AddFlowBus();
// ─────────────────────────────────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<WorkflowTestBootstrap>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

static UseCaseRuntimeOptions BuildUseCaseRuntimeOptions(WorkflowAdminOptions admin, ConsumerServiceOptions consumer) {
    return new UseCaseRuntimeOptions {
        EnvCode = consumer.EnvCode,
        ConfirmationTimeout = TimeSpan.FromSeconds(Math.Max(0, admin.ConfirmationTimeoutSeconds))
    };
}
