using WFE.AdminApi.Configuration;
using WFE.AdminApi.Services;
using Haley.Abstractions;
using Haley.Services;
using Haley.Utils;
using Microsoft.Extensions.Options;
using WFE.Test;
using WFE.Test.UseCases.ChangeRequest;
using WFE.Test.UseCases.LoanApproval;
using WFE.Test.UseCases.PaperlessReview;
using WFE.Test.UseCases.VendorRegistration;

var builder = WebApplication.CreateBuilder(args);
var defaults = new UseSettingsBase();
var adminSection = builder.Configuration.GetSection("WorkflowAdmin");
var adminBootstrap = adminSection.Get<WorkflowAdminOptions>() ?? new WorkflowAdminOptions();

builder.Services.Configure<WorkflowAdminOptions>(adminSection);
builder.Services.AddWorkFlowEngineService(opt => {
    opt.EngineAdapterKey = adminBootstrap.EngineAdapterKey;
    opt.MonitorInterval = defaults.MonitorInterval;
    opt.AckPendingResendAfter = defaults.AckPendingResendAfter;
    opt.AckDeliveredResendAfter = defaults.AckDeliveredResendAfter;
    opt.MaxRetryCount = defaults.MaxRetryCount;
    opt.ConsumerTtlSeconds = defaults.ConsumerTtlSeconds;
    opt.ConsumerDownRecheckSeconds = defaults.ConsumerDownRecheckSeconds;
}, autoStart: true);

builder.Services.AddWorkFlowConsumerBootstrap(opt => {
    opt.ConsumerAdapterKey = adminBootstrap.ConsumerAdapterKey;
    opt.EnvCode = adminBootstrap.EnvCode;
    opt.EnvDisplayName = adminBootstrap.EnvDisplayName;
    opt.ConsumerGuid = adminBootstrap.ConsumerGuid;
    opt.BatchSize = defaults.ConsumerBatchSize;
    opt.PollInterval = defaults.ConsumerPollInterval;
    opt.HeartbeatInterval = defaults.ConsumerHeartbeatInterval;
    opt.TtlSeconds = defaults.ConsumerTtlSeconds;
    opt.WrapperAssemblies = new List<string> {
        typeof(ChangeRequestWrapper).Assembly.GetName().Name ?? "WFE.Lib"
    };
}, autoStart: false, addDeferredInProcessProxy: true);

builder.Services.AddSingleton<IWorkFlowEngine>(sp =>
    sp.GetRequiredService<IWorkFlowEngineAccessor>().GetEngineAsync(CancellationToken.None).GetAwaiter().GetResult());

builder.Services.AddSingleton(sp => BuildSettings(sp.GetRequiredService<IOptions<WorkflowAdminOptions>>().Value, new ChangeRequestUseCaseSettings()));
builder.Services.AddSingleton(sp => BuildSettings(sp.GetRequiredService<IOptions<WorkflowAdminOptions>>().Value, new LoanApprovalUseCaseSettings()));
builder.Services.AddSingleton(sp => BuildSettings(sp.GetRequiredService<IOptions<WorkflowAdminOptions>>().Value, new PaperlessReviewUseCaseSettings()));
builder.Services.AddSingleton(sp => BuildSettings(sp.GetRequiredService<IOptions<WorkflowAdminOptions>>().Value, new VendorRegistrationUseCaseSettings()));
builder.Services.AddTransient<ChangeRequestWrapper>();
builder.Services.AddTransient<LoanApprovalWrapper>();
builder.Services.AddTransient<PaperlessReviewWrapper>();
builder.Services.AddTransient<VendorRegistrationWrapper>();

builder.Services.AddSingleton<IWorkflowAdminService, WorkflowAdminService>();
builder.Services.AddHostedService<WorkflowAdminHostedService>();
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

static T BuildSettings<T>(WorkflowAdminOptions source, T target) where T : UseSettingsBase {
    target.EnvCode = source.EnvCode;
    target.EnvDisplayName = source.EnvDisplayName;
    target.ConsumerGuid = source.ConsumerGuid;
    target.ENGINE_DBNAME = source.EngineAdapterKey;
    target.CONSUMER_DBNAME = source.ConsumerAdapterKey;
    target.ConfirmationTimeout = TimeSpan.FromSeconds(Math.Max(0, source.ConfirmationTimeoutSeconds));
    return target;
}
