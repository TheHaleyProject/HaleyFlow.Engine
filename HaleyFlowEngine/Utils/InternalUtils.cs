using Haley.Abstractions;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Haley.Utils {
    internal static class SqlUtil {
        public static bool PolicyContainsStateRoute(string policyJson, string stateName) {
            try {
                using var doc = JsonDocument.Parse(policyJson);
                if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array)
                    return false;

                foreach (var r in routes.EnumerateArray()) {
                    if (!r.TryGetProperty("state", out var s) || s.ValueKind != JsonValueKind.String) continue;
                    var sn = s.GetString();
                    if (sn != null && string.Equals(sn, stateName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            } catch {
                return false;
            }
        }
    }

    internal static class JsonUtil {
        public static IReadOnlyDictionary<string, object?>? TryParseDictionary(string? json) {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in doc.RootElement.EnumerateObject())
                    dict[p.Name] = ToObject(p.Value);

                return dict;
            } catch {
                return null;
            }
        }

        private static object? ToObject(JsonElement el) {
            return el.ValueKind switch {
                JsonValueKind.Null => null,
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Array => el.EnumerateArray().Select(ToObject).ToList(),
                JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => ToObject(p.Value), StringComparer.OrdinalIgnoreCase),
                _ => el.GetRawText()
            };
        }
    }

    internal static class DbRowExtensions {
        public static bool TryGetValue(this DbRow row, string key, out object? value) {
            // DbRow is expected to behave like Dictionary<string, object>
            if (row is IDictionary<string, object?> dictObj)
                return dictObj.TryGetValue(key, out value);

            // fallback (common in legacy dictionary implementations)
            try {
                value = row[key];
                return value != null || row.ContainsKey(key);
            } catch {
                value = null;
                return false;
            }
        }

        public static string? GetString(this DbRow row, string key) {
            if (!row.TryGetValue(key, out var v) || v == null) return null;
            return v.ToString();
        }

        public static long GetInt64(this DbRow row, string key) {
            if (!row.TryGetValue(key, out var v) || v == null) return 0;
            return Convert.ToInt64(v);
        }

        public static long? GetNullableInt64(this DbRow row, string key) {
            if (!row.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToInt64(v);
        }

        public static int GetInt32(this DbRow row, string key) {
            if (!row.TryGetValue(key, out var v) || v == null) return 0;
            return Convert.ToInt32(v);
        }

        public static int? GetNullableInt32(this DbRow row, string key) {
            if (!row.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToInt32(v);
        }
    }

}
