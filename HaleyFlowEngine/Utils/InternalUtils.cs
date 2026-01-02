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
        public static async Task<long> LastInsertIdAsync(IWorkFlowDALUtil db, DbExecutionLoad load) {
           return await db.ScalarAsync<long>("SELECT LAST_INSERT_ID();", load);
        }

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
}
