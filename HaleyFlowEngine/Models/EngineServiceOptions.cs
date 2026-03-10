using Microsoft.Extensions.Configuration;

namespace Haley.Models;
public sealed class EngineServiceOptions : WorkFlowEngineOptions {
    [ConfigurationKeyName("env_code")]
    public int EnvCode { get; set; } = 1000;
    [ConfigurationKeyName("env_name")]
    public string EnvDisplayName { get; set; } = "dev";
    public int DefaultTake { get; set; } = 50; //pagination takes
    public int MaxTake { get; set; } = 500; //Pagination maximum take allowed. We should not allow user to take 10000 items, it defies the logic of pagination then.
    [ConfigurationKeyName("consumer_guid")]
    public string ConsumerGuid { get; set; } = string.Empty;
    [ConfigurationKeyName("adapter_key")]
    public string EngineAdapterKey { get; set; } = string.Empty;
}
