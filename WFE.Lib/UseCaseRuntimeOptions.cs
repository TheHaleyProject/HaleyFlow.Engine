namespace WFE.Test;

public sealed class UseCaseRuntimeOptions {
    public int EnvCode { get; set; } = 1000;
    public TimeSpan ConfirmationTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
