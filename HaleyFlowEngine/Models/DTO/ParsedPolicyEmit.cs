using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    // Pre-materialized form of one emit entry inside a policy rule.
    // Built once during ParseJsonToPolicy; immutable and thread-safe.
    internal sealed class ParsedPolicyEmit {
        public string Route { get; init; } = "";
        public HookType? Type { get; init; }         // null = inherit from rule
        public string? Group { get; init; }
        public int OrderSeq { get; init; } = 1;
        public int AckMode { get; init; }           // 0 = all, 1 = any
        public string? OnSuccess { get; init; }     // already collapsed from rule fallback
        public string? OnFailure { get; init; }
        public IReadOnlyList<string> ParamCodes { get; init; } = Array.Empty<string>();
        public DateTimeOffset? NotBefore { get; init; }
        public DateTimeOffset? Deadline { get; init; }
    }
}
