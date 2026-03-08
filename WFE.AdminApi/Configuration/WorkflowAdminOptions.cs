namespace WFE.AdminApi.Configuration;

public sealed class WorkflowAdminOptions {
    public int EnvCode { get; set; } = 1000;
    public string EnvDisplayName { get; set; } = "dev";
    public int DefaultTake { get; set; } = 50;
    public int MaxTake { get; set; } = 500;
    public string EngineAdapterKey { get; set; } = "lce_test";
    public string ConsumerAdapterKey { get; set; } = "lcc_test";
    public string ConsumerGuid { get; set; } = "89c52807-5054-47fc-9dee-dbb8b42218cb";
    public int ConfirmationTimeoutSeconds { get; set; } = 4;
    public string UseCasesRootPath { get; set; } = @"..\..\..\..\WFE.Test\UseCases";
}
