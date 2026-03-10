namespace Haley.Models;

public sealed class ReopenInstanceRequest {
    public string InstanceGuid { get; set; } = string.Empty;
    public string? Actor { get; set; }
}