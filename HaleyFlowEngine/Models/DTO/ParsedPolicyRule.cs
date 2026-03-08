using Haley.Enums;
using Haley.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    // Pre-materialized form of one policy rule.
    internal sealed class ParsedPolicyRule {
        public string State { get; init; } = "";
        public int? Via { get; init; }              // null = match any triggering event
        public bool? Blocking { get; init; }        // null = engine default (true)
        public string? OnSuccess { get; init; }
        public string? OnFailure { get; init; }
        public IReadOnlyList<string> ParamCodes { get; init; } = Array.Empty<string>();
        public IReadOnlyList<ParsedPolicyEmit> Emits { get; init; } = Array.Empty<ParsedPolicyEmit>();
    }
}
