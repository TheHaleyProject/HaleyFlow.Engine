namespace WFE.AdminApi.Configuration;

public sealed class WorkflowAdminOptions {
    public int EnvCode { get; set; } = 1000;
    public string EnvDisplayName { get; set; } = "dev";
    public int DefaultTake { get; set; } = 50;
    public int MaxTake { get; set; } = 500;
}
