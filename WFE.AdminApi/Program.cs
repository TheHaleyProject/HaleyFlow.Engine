using WFE.AdminApi.Configuration;
using WFE.AdminApi.Services;
using Haley.Abstractions;
using Haley.Utils;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<WorkflowAdminOptions>(builder.Configuration.GetSection("WorkflowAdmin"));
builder.Services.AddSingleton<IAdapterGateway, AdapterGateway>();
builder.Services.AddSingleton<IWorkflowAdminService, WorkflowAdminService>();
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
