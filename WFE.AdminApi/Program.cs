using WFE.AdminApi.Configuration;
using WFE.AdminApi.Services;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Options;
using WFE.Test;
using Haley.Enums;
using WFE.Test.UseCases.ChangeRequest;
using WFE.Test.UseCases.LoanApproval;
using WFE.Test.UseCases.PaperlessReview;
using WFE.Test.UseCases.VendorRegistration;

var builder = WebApplication.CreateBuilder(args);
var adminSection = builder.Configuration.GetSection("WorkflowAdmin");

builder.Services.Configure<WorkflowAdminOptions>(adminSection);
// Startup ownership stays with WorkflowTestBootstrapHostedService so engine import/consumer init
// happen in a single ordered path during app boot.
builder.Services.AddWorkFlowEngineService(builder.Configuration, autoStart: false, resolveConsumerGuids: async (ty,envCode,defName,cts) => {
    //what ever is the value, at th moment, let us return the same guid for testing purpose.
    return  new List<string> { "89c52807-5054-47fc-9dee-dbb8b42218cb" };
});
builder.Services.AddInProcessEngineProxy();

// If autoStart=true, consumer can start polling before the test definitions/policies import finishes.
builder.Services.AddWorkFlowConsumerService(builder.Configuration, sectionName: "WorkFlowConsumer", autoStart: false);

builder.Services.AddSingleton(sp => BuildUseCaseRuntimeOptions(
    sp.GetRequiredService<IOptions<WorkflowAdminOptions>>().Value,
    sp.GetRequiredService<IOptions<ConsumerServiceOptions>>().Value));

// WFE.Lib wrapper types take UseCaseRuntimeOptions — register as transient so
// WorkFlowConsumerManager can resolve them via _sp.GetService (not Activator.CreateInstance).
builder.Services.AddTransient<ChangeRequestWrapper>();
builder.Services.AddTransient<LoanApprovalWrapper>();
builder.Services.AddTransient<VendorRegistrationWrapper>();
builder.Services.AddTransient<PaperlessReviewWrapper>();

builder.Services.AddSingleton<WorkflowTestBootstrap>();
builder.Services.AddHostedService<WorkflowTestBootstrapHostedService>();
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
