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
using static Haley.Internal.KeyConstants;

namespace Haley.Utils {
    internal static class InternalUtils {

        public static string BuildDefinitionHashMaterial(this JsonElement root) {
            // keep ONLY states/events/transitions
            var obj = new JsonObject {
                [KEY_STATES] = BuildSanitizedStatesForHash(root),
                [KEY_EVENTS] = root.TryGetProperty(KEY_EVENTS, out var e) ? JsonNode.Parse(e.GetRawText()) : new JsonArray(),
                [KEY_TRANSITIONS] = root.TryGetProperty(KEY_TRANSITIONS, out var t) ? JsonNode.Parse(t.GetRawText()) : new JsonArray(),
            };

            var canon = obj.Canonicalize(); //ignore case..
            return canon.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        public static string BuildPolicyHashMaterial(this JsonElement root) {
            // keep ONLY policy_name/rules/params/timeouts (ignore "for")
            var obj = new JsonObject {
                [KEY_POLICY_NAME] = root.TryGetProperty(KEY_POLICY_NAME, out var pn) ? pn.GetString() : (string?)null,
                [KEY_RULES] = root.TryGetProperty(KEY_RULES, out var p) ? JsonNode.Parse(p.GetRawText()) : new JsonArray(),
                [KEY_PARAMS] = root.TryGetProperty(KEY_PARAMS, out var r) ? JsonNode.Parse(r.GetRawText()) : new JsonArray(),
                [KEY_TIMEOUTS] = root.TryGetProperty(KEY_TIMEOUTS, out var to) ? JsonNode.Parse(to.GetRawText()) : new JsonArray(),
            };

            var canon = obj.Canonicalize();
            return canon.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        static JsonArray BuildSanitizedStatesForHash(JsonElement root) {
            if (!root.TryGetProperty(KEY_STATES, out var statesEl) || statesEl.ValueKind != JsonValueKind.Array)
                return new JsonArray();

            var arr = new JsonArray();

            foreach (var s in statesEl.EnumerateArray()) {
                if (s.ValueKind != JsonValueKind.Object) continue;

                // Copy state object, excluding timeout-ish keys
                var o = new JsonObject();
                foreach (var p in s.EnumerateObject()) {
                    var k = p.Name;

                    if (k.Equals(KEY_TIMEOUT, StringComparison.OrdinalIgnoreCase) ||
                        k.Equals(KEY_TIMEOUT_MINUTES, StringComparison.OrdinalIgnoreCase) ||
                        k.Equals(KEY_TIMEOUT_MINUTES_CAMEL, StringComparison.OrdinalIgnoreCase) ||
                        k.Equals(KEY_TIMEOUT_MODE, StringComparison.OrdinalIgnoreCase) ||
                        k.Equals(KEY_TIMEOUT_MODE_CAMEL, StringComparison.OrdinalIgnoreCase) ||
                        k.Equals(KEY_TIMEOUT_EVENT, StringComparison.OrdinalIgnoreCase) ||
                        k.Equals(KEY_TIMEOUT_EVENT_CAMEL, StringComparison.OrdinalIgnoreCase))
                        continue;

                    o[k] = JsonNode.Parse(p.Value.GetRawText());
                }

                arr.Add(o);
            }

            return arr;
        }

    }
}
