using Haley.Abstractions;
using Haley.Internal;
using Haley.Models;
using System;
using System;
using Haley.Enums;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Haley.Services {
    public sealed class BlueprintImporter : IBlueprintImporter {
        private readonly IWorkFlowDAL _dal;
        public BlueprintImporter(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }
        public async Task<long> ImportDefinitionJsonAsync(int envCode, string envDisplayName, string definitionJson, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(definitionJson)) throw new ArgumentNullException(nameof(definitionJson));

            using var doc = JsonDocument.Parse(definitionJson);
            var root = doc.RootElement;

            var defNode = root.TryGetProperty("definition", out var d) ? d : root;
            var defName = ReqString(defNode, "name") ?? ReqString(defNode, "displayName") ?? ReqString(defNode, "defName") ?? throw new InvalidOperationException("definition.name/displayName missing.");
            var defDesc = TryString(defNode, "description");
            var requestedVer = TryInt(defNode, "version") ?? 0;

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;

            try {
                var envId = await _dal.BlueprintWrite.EnsureEnvironmentByCodeAsync(envCode, envDisplayName, load);
                var defId = await _dal.BlueprintWrite.EnsureDefinitionByEnvIdAsync(envId, defName, defDesc, load);

                var nextVer = await _dal.Blueprint.GetNextDefVersionNumberByEnvCodeAndDefNameAsync(envCode, defName, load) ?? 1;
                var verToUse = requestedVer > 0 ? requestedVer : nextVer;
                if (requestedVer > 0 && requestedVer != nextVer) throw new InvalidOperationException($"JSON version={requestedVer} but DB next_version={nextVer}. Import rejected.");

                var defVersionId = await _dal.BlueprintWrite.InsertDefVersionAsync(defId, verToUse, definitionJson, load);

                var categoryMap = await ImportCategoriesFromStatesAsync(root, load);
                var eventsByCode = await ImportEventsAsync(defVersionId, root, load);
                var statesByName = await ImportStatesAsync(defVersionId, root, categoryMap, eventsByCode, load);
                await ImportTransitionsAsync(defVersionId, root, statesByName, eventsByCode, load);

                tx.Commit();
                committed = true;
                return defVersionId;
            } catch {
                if (!committed) tx.Rollback();
                throw;
            }
        }

        public async Task<long> ImportPolicyJsonAsync(int envCode, string envDisplayName, string policyJson, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(policyJson)) throw new ArgumentNullException(nameof(policyJson));

            using var doc = JsonDocument.Parse(policyJson);
            var root = doc.RootElement;

            var defName = TryString(root, "defName") ?? TryString(root, "definitionName") ?? TryString(root, "name") ?? TryString(root, "displayName");
            if (string.IsNullOrWhiteSpace(defName)) throw new InvalidOperationException("Policy JSON missing defName/definitionName/name/displayName.");

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;
            try {
                var envId = await _dal.BlueprintWrite.EnsureEnvironmentByCodeAsync(envCode, envDisplayName, load);
                _ = await _dal.BlueprintWrite.EnsureDefinitionByEnvIdAsync(envId, defName!, description: null, load);

                var hash = Hash48(policyJson);
                var policyId = await _dal.BlueprintWrite.EnsurePolicyByHashAsync(hash, policyJson, load);

                await _dal.BlueprintWrite.AttachPolicyToDefinitionByEnvCodeAndDefNameAsync(envCode, defName!, policyId, load);

                tx.Commit();
                committed = true;
                return policyId;
            } catch {
                if (!committed) tx.Rollback();
                throw;
            }
        }

        private async Task<Dictionary<string, int>> ImportCategoriesFromStatesAsync(JsonElement root, DbExecutionLoad load) {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("states", out var states) || states.ValueKind != JsonValueKind.Array) return map;

            foreach (var s in states.EnumerateArray()) {
                var cat = TryString(s, "category");
                if (string.IsNullOrWhiteSpace(cat)) continue;

                var key = N(cat);
                if (map.ContainsKey(key)) continue;

                var id = await _dal.BlueprintWrite.EnsureCategoryByNameAsync(cat!, load);
                map[key] = id;
            }

            return map;
        }

        private async Task<Dictionary<int, EventDef>> ImportEventsAsync(long defVersionId, JsonElement root, DbExecutionLoad load) {
            var byCode = new Dictionary<int, EventDef>();
            if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) return byCode;

            foreach (var e in events.EnumerateArray()) {
                var code = TryInt(e, "code") ?? 0;
                var name = TryString(e, "name") ?? TryString(e, "displayName");
                if (code <= 0 || string.IsNullOrWhiteSpace(name)) continue;
                if (byCode.ContainsKey(code)) throw new InvalidOperationException($"Duplicate event code in JSON: {code}");

                var id = await _dal.BlueprintWrite.InsertEventAsync(defVersionId, name!, code, load);
                byCode[code] = new EventDef { Id = id, Code = code, Name = N(name!), DisplayName = name! };
            }

            return byCode;
        }

        private async Task<Dictionary<string, StateDef>> ImportStatesAsync(long defVersionId, JsonElement root, Dictionary<string, int> categoryMap, Dictionary<int, EventDef> eventsByCode, DbExecutionLoad load) {
            var map = new Dictionary<string, StateDef>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("states", out var states) || states.ValueKind != JsonValueKind.Array) return map;

            foreach (var s in states.EnumerateArray()) {
                var name = TryString(s, "name") ?? TryString(s, "displayName");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var key = N(name!);
                if (map.ContainsKey(key)) throw new InvalidOperationException($"Duplicate state name in JSON: {name}");

                var flags = (uint)(TryInt(s, "flags") ?? 0);
                if (TryBool(s, "is_initial") == true) flags |= (uint)LifeCycleStateFlag.IsInitial;
                if (TryBool(s, "is_final") == true) flags |= (uint)LifeCycleStateFlag.IsFinal;

                var timeoutMinutes = ParseTimeoutMinutes(s);
                var timeoutMode = ParseTimeoutMode(s);

                long timeoutEventId = 0;
                var timeoutEventCode = TryInt(s, "timeout_event") ?? TryInt(s, "timeoutEventCode");
                if (timeoutEventCode.HasValue && eventsByCode.TryGetValue(timeoutEventCode.Value, out var tev)) timeoutEventId = tev.Id;

                var catName = TryString(s, "category");
                var catId = (!string.IsNullOrWhiteSpace(catName) && categoryMap.TryGetValue(N(catName!), out var cid)) ? cid : 0;

                var id = await _dal.BlueprintWrite.InsertStateAsync(defVersionId, catId, name!, flags, timeoutMinutes, (uint)timeoutEventId, timeoutMode, load);

                map[key] = new StateDef {
                    Id = id,
                    Name = key,
                    DisplayName = name!,
                    Flags = flags,
                    TimeoutMinutes = timeoutMinutes,
                    TimeoutEventId = timeoutEventId,
                    IsInitial = (flags & (uint)LifeCycleStateFlag.IsInitial) != 0
                };
            }

            return map;
        }

        private async Task ImportTransitionsAsync(long defVersionId, JsonElement root, Dictionary<string, StateDef> statesByName, Dictionary<int, EventDef> eventsByCode, DbExecutionLoad load) {
            if (!root.TryGetProperty("transitions", out var trans) || trans.ValueKind != JsonValueKind.Array) return;

            foreach (var t in trans.EnumerateArray()) {
                var fromName = TryString(t, "from") ?? TryString(t, "fromState");
                var toName = TryString(t, "to") ?? TryString(t, "toState");
                var evCode = TryInt(t, "event") ?? TryInt(t, "eventCode");
                if (string.IsNullOrWhiteSpace(fromName) || string.IsNullOrWhiteSpace(toName) || !evCode.HasValue) continue;

                if (!statesByName.TryGetValue(N(fromName!), out var from)) throw new InvalidOperationException($"Transition from-state not found: {fromName}");
                if (!statesByName.TryGetValue(N(toName!), out var to)) throw new InvalidOperationException($"Transition to-state not found: {toName}");
                if (!eventsByCode.TryGetValue(evCode.Value, out var ev)) throw new InvalidOperationException($"Transition event code not found: {evCode}");

                var flags = (uint)(TryInt(t, "flags") ?? 0);
                await _dal.BlueprintWrite.InsertTransitionAsync(defVersionId, from.Id, to.Id, ev.Id, load);
            }
        }

        private static int? ParseTimeoutMinutes(JsonElement stateNode) {
            var tm = TryInt(stateNode, "timeout_minutes") ?? TryInt(stateNode, "timeoutMinutes");
            if (tm.HasValue) return tm;

            var dur = TryString(stateNode, "timeout");
            if (string.IsNullOrWhiteSpace(dur)) return null;

            var ts = XmlConvert.ToTimeSpan(dur!);
            var mins = (int)Math.Ceiling(ts.TotalMinutes);
            return mins <= 0 ? null : mins;
        }

        private static int ParseTimeoutMode(JsonElement stateNode) {
            var n = TryInt(stateNode, "timeout_mode") ?? TryInt(stateNode, "timeoutMode");
            if (n.HasValue) return n.Value;

            var s = TryString(stateNode, "timeout_mode") ?? TryString(stateNode, "timeoutMode");
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return string.Equals(s.Trim(), "repeat", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private static string N(string s) => (s ?? string.Empty).Trim().ToLowerInvariant();

        private static string? ReqString(JsonElement e, string prop) => TryString(e, prop);

        private static string? TryString(JsonElement e, string prop) {
            if (!e.TryGetProperty(prop, out var v)) return null;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        }

        private static int? TryInt(JsonElement e, string prop) {
            if (!e.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j)) return j;
            return null;
        }

        private static bool? TryBool(JsonElement e, string prop) {
            if (!e.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
            return null;
        }

        private static string Hash48(string input) {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
            var hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            return hex.Length <= 48 ? hex : hex.Substring(0, 48);
        }
    }

}
