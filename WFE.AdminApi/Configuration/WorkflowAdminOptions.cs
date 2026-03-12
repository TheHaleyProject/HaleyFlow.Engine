namespace WFE.AdminApi.Configuration;

public sealed class WorkflowAdminOptions {
    public int DefaultTake { get; set; } = 50;
    public int MaxTake { get; set; } = 500;
    public int ConfirmationTimeoutSeconds { get; set; } = 4;
    public string UseCasesRootPath { get; set; } = @"..\..\..\..\WFE.Lib\UseCases";
}
