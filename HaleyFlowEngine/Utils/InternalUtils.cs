namespace Haley.Utils {
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using static Haley.Internal.KeyConstants;

    /// <summary>
    /// Defines the <see cref="InternalUtils" />
    /// </summary>
    internal static class InternalUtils {
        /// <summary>
        /// The NormalizeConsumers
        /// </summary>
        /// <param name="consumerIds">The consumerIds<see cref="IReadOnlyList{long}?"/></param>
        /// <returns>The <see cref="IReadOnlyList{long}"/></returns>
        public static IReadOnlyList<long> NormalizeConsumers(IReadOnlyList<long>? consumerIds) {
            if (consumerIds == null || consumerIds.Count == 0) return Array.Empty<long>();

            var set = new HashSet<long>();
            for (var i = 0; i < consumerIds.Count; i++) {
                var consumerId = consumerIds[i];
                if (consumerId > 0) set.Add(consumerId);
            }

            if (set.Count == 0) return Array.Empty<long>();
            var result = new long[set.Count];
            set.CopyTo(result);
            return result;
        }

        /// <summary>
        /// The NormalizeRuntimeName
        /// </summary>
        /// <param name="value">The value<see cref="string?"/></param>
        /// <returns>The <see cref="string"/></returns>
        public static string NormalizeRuntimeName(string? value) {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant();
        }

        /// <summary>
        /// The BuildDefinitionHashMaterial
        /// </summary>
        /// <param name="root">The root<see cref="JsonElement"/></param>
        /// <returns>The <see cref="string"/></returns>
        public static string BuildDefinitionHashMaterial(this JsonElement root) {
            // keep ONLY states/events/transitions
            var obj = new JsonObject {
                [KEY_STATES] = BuildSanitizedStatesForHash(root),
                [KEY_EVENTS] = root.TryGetProperty(KEY_EVENTS, out var e) ? JsonNode.Parse(e.GetRawText()) : new JsonArray(),
                [KEY_TRANSITIONS] = root.TryGetProperty(KEY_TRANSITIONS, out var t) ? JsonNode.Parse(t.GetRawText()) : new JsonArray(),
            };

            var canon = StripDescriptions(obj).Canonicalize(); //ignore case..
            return canon.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        /// <summary>
        /// The BuildPolicyHashMaterial
        /// </summary>
        /// <param name="root">The root<see cref="JsonElement"/></param>
        /// <returns>The <see cref="string"/></returns>
        public static string BuildPolicyHashMaterial(this JsonElement root) {
            // keep ONLY policy_name/rules/params/timeouts (ignore "for")
            var obj = new JsonObject {
                [KEY_POLICY_NAME] = root.TryGetProperty(KEY_POLICY_NAME, out var pn) ? pn.GetString() : (string?)null,
                [KEY_RULES] = root.TryGetProperty(KEY_RULES, out var p) ? JsonNode.Parse(p.GetRawText()) : new JsonArray(),
                [KEY_PARAMS] = root.TryGetProperty(KEY_PARAMS, out var r) ? JsonNode.Parse(r.GetRawText()) : new JsonArray(),
                [KEY_TIMEOUTS] = root.TryGetProperty(KEY_TIMEOUTS, out var to) ? JsonNode.Parse(to.GetRawText()) : new JsonArray(),
            };

            var canon = StripDescriptions(obj).Canonicalize();
            return canon.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        static JsonNode StripDescriptions(JsonNode? node) {
            if (node is JsonObject obj) {
                var clean = new JsonObject();
                foreach (var kv in obj) {
                    if (kv.Key.Equals(KEY_DESCRIPTION, StringComparison.OrdinalIgnoreCase)) continue;
                    clean[kv.Key] = StripDescriptions(kv.Value);
                }
                return clean;
            }
            if (node is JsonArray arr) {
                var clean = new JsonArray();
                foreach (var item in arr) clean.Add(StripDescriptions(item));
                return clean;
            }
            return node?.DeepClone() ?? JsonValue.Create((string?)null)!;
        }

        internal static JsonArray BuildSanitizedStatesForHash(JsonElement root) {
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
                        k.Equals(KEY_TIMEOUT_EVENT_CAMEL, StringComparison.OrdinalIgnoreCase) ||
                        k.Equals(KEY_DESCRIPTION, StringComparison.OrdinalIgnoreCase))
                        continue;

                    o[k] = JsonNode.Parse(p.Value.GetRawText());
                }

                arr.Add(o);
            }

            return arr;
        }
    }
}
