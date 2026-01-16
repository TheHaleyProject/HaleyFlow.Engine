using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace Haley.Utils {
    internal static class InternalUtils {
        public static string BuildDefinitionHashMaterial(this JsonElement root) {
            // keep ONLY states/events/transitions
            var obj = new JsonObject {
                ["states"] = BuildSanitizedStatesForHash(root),
                ["events"] = root.TryGetProperty("events", out var e) ? JsonNode.Parse(e.GetRawText()) : new JsonArray(),
                ["transitions"] = root.TryGetProperty("transitions", out var t) ? JsonNode.Parse(t.GetRawText()) : new JsonArray(),
            };

            var canon = obj.Canonicalize(); //ignore case..
            return canon.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        public static string BuildPolicyHashMaterial(this JsonElement root) {
            // keep ONLY policy_name/policies/routes (ignore "for")
            var obj = new JsonObject {
                ["policy_name"] = root.TryGetProperty("policy_name", out var pn) ? pn.GetString() : (string?)null,
                ["policies"] = root.TryGetProperty("policies", out var p) ? JsonNode.Parse(p.GetRawText()) : new JsonArray(),
                ["routes"] = root.TryGetProperty("routes", out var r) ? JsonNode.Parse(r.GetRawText()) : new JsonArray(),
                ["timeouts"] = root.TryGetProperty("timeouts", out var to) ? JsonNode.Parse(to.GetRawText()) : new JsonArray(),
            };

            var canon = obj.Canonicalize();
            return canon.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        static JsonArray BuildSanitizedStatesForHash(JsonElement root) {
            if (!root.TryGetProperty("states", out var statesEl) || statesEl.ValueKind != JsonValueKind.Array)
                return new JsonArray();

            var arr = new JsonArray();

            foreach (var s in statesEl.EnumerateArray()) {
                if (s.ValueKind != JsonValueKind.Object) continue;

                // Copy state object, excluding timeout-ish keys
                var o = new JsonObject();
                foreach (var p in s.EnumerateObject()) {
                    var k = p.Name;

                    if (k.Equals("timeout", StringComparison.OrdinalIgnoreCase) ||
                        k.Equals("timeout_minutes", StringComparison.OrdinalIgnoreCase) ||
                        k.Equals("timeout_mode", StringComparison.OrdinalIgnoreCase) ||
                        k.Equals("timeout_event", StringComparison.OrdinalIgnoreCase) ||
                        k.Equals("timeoutEventCode", StringComparison.OrdinalIgnoreCase))
                        continue;

                    o[k] = JsonNode.Parse(p.Value.GetRawText());
                }

                arr.Add(o);
            }

            return arr;
        }

    }
}
