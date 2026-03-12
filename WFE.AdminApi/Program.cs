using WFE.AdminApi.Configuration;
using WFE.AdminApi.Services;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Options;
using WFE.Test;

var builder = WebApplication.CreateBuilder(args);
var adminSection = builder.Configuration.GetSection("WorkflowAdmin");

builder.Services.Configure<WorkflowAdminOptions>(adminSection);
builder.Services.AddWorkFlowEngineService(builder.Configuration);

// If autoStart=true, consumer can start polling before the test definitions/policies import finishes.
builder.Services.AddWorkFlowConsumerService(builder.Configuration, sectionName: "WorkFlowConsumer", autoStart: false, addDeferredInProcessProxy: true);

builder.Services.AddSingleton(sp => BuildUseCaseRuntimeOptions(
    sp.GetRequiredService<IOptions<WorkflowAdminOptions>>().Value,
    sp.GetRequiredService<IOptions<ConsumerServiceOptions>>().Value));

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
