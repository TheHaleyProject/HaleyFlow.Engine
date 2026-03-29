using Haley.Enums;
using Microsoft.Extensions.Configuration;

namespace Haley.Models;

// Engine host options only.
// Consumer identity/environment options belong to consumer-side initiator options.
public sealed class EngineServiceOptions : WorkFlowEngineOptions {
    [ConfigurationKeyName("adapter_key")]
    public string EngineAdapterKey { get; set; } = string.Empty;

    [ConfigurationKeyName("recent_notice_retention")]
    public TimeSpan RecentNoticeRetention { get; set; } = TimeSpan.FromHours(2);

    [ConfigurationKeyName("recent_notice_max_count")]
    public int RecentNoticeMaxCount { get; set; } = 1000;
}
