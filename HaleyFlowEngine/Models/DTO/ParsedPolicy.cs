using Haley.Enums;
using Haley.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    // Fully materialized in-memory representation of a policy JSON document.
    // Built once per policyId (per process lifetime) and cached in PolicyEnforcer._policyCache.
    // Immutable after construction — safe to share across threads without locking.
    internal sealed class ParsedPolicy {
        public IReadOnlyList<ParsedPolicyRule> Rules { get; init; } = Array.Empty<ParsedPolicyRule>();
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>?> ParamCatalog { get; init; }
            = new Dictionary<string, IReadOnlyDictionary<string, object?>?>(StringComparer.OrdinalIgnoreCase);
    }
}
