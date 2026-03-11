using Haley.Enums;
using Microsoft.Extensions.Configuration;

namespace Haley.Models;

// Engine host options only.
// Consumer identity/environment options belong to consumer-side initiator options.
public sealed class EngineBootstrapOptions : WorkFlowEngineOptions {
    [ConfigurationKeyName("adapter_key")]
    public string EngineAdapterKey { get; set; } = string.Empty;
}
