using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Haley.Utils;
using System;
using System;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Xml;

namespace Haley.Services {
    internal sealed class BlueprintImporter : IBlueprintImporter {
        private readonly IWorkFlowDAL _dal;
        public BlueprintImporter(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }
        public async Task<long> ImportDefinitionJsonAsync(int envCode, string envDisplayName, string definitionJson, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(definitionJson)) throw new ArgumentNullException(nameof(definitionJson));

            using var doc = JsonDocument.Parse(definitionJson);
            var root = doc.RootElement;

            var defNode = root.TryGetProperty("definition", out var d) ? d : root;
            var defName = defNode.GetString("name") ?? defNode.GetString("displayName") ?? defNode.GetString("defName") ?? throw new InvalidOperationException("definition.name/displayName missing.");
            var defDesc = defNode.GetString("description");
            var requestedVer = defNode.GetInt("version") ?? 0;



            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;

            try {
                var envId = await _dal.BlueprintWrite.EnsureEnvironmentByCodeAsync(envCode, envDisplayName, load);
                var defId = await _dal.BlueprintWrite.EnsureDefinitionByEnvIdAsync(envId, defName, defDesc, load);

                //Check if the definition has the version json already with the hash.
                var defhashMaterial = root.BuildDefinitionHashMaterial();
                var defHash = defhashMaterial.CreateGUID(HashMethod.Sha256).ToString();
                var existing = await _dal.Blueprint.GetDefVersionByParentAndHashAsync(defId, defHash, load);
                if (existing != null) {
                    tx.Commit();
                    committed = true;
                    return existing.GetLong("id"); //Definition version already exists with the same hash. Return existing version id.
                }

                var nextVer = await _dal.Blueprint.GetNextDefVersionNumberByEnvCodeAndDefNameAsync(envCode, defName, load) ?? 1;
                var verToUse = requestedVer > 0 ? requestedVer : nextVer; //If version is not specified in the json, then automatically, next available version is accepted.
                if (requestedVer > 0 && requestedVer < nextVer) throw new InvalidOperationException($"JSON version={requestedVer} but DB next_version={nextVer}. Import rejected. Requested version should either be equal to or greater than the next available version. Leave version empty in the json to automatically assign the version.");

                var defVersionId = await _dal.BlueprintWrite.InsertDefVersionAsync(defId, verToUse, defhashMaterial, defHash, load);

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

        async Task ImportPolicyTimeoutsAsync(long policyId, JsonElement root, DbExecutionLoad load) {
            //// Always clear first to make policy re-import idempotent. Remember we are deleting by policy id, which means it wont affect the other policies even if they share same definition.
            await _dal.BlueprintWrite.DeleteByPolicyIdAsync(policyId, load);

            if (!root.TryGetProperty("timeouts", out var arr) || arr.ValueKind != JsonValueKind.Array) return;

            foreach (var t in arr.EnumerateArray()) {
                if (t.ValueKind != JsonValueKind.Object) continue;

                var stateRaw = t.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
                if (string.IsNullOrWhiteSpace(stateRaw)) continue;

                var stateName = stateRaw.Normalize(true);             
                var durationMinutes = ParseTimeoutMinutes(t) ?? 0; // "P2D" -> 2880
                var mode = ParseTimeoutMode(t);                         // 0 once, 1 repeat
                var eventCode = ParseTimeoutEventCode(t);               // nullable

                // You can choose to skip invalid durations, or throw. I prefer throw during import.
                if (durationMinutes <= 0) throw new ArgumentException($"Invalid timeout duration for state '{stateRaw}'.");

                await _dal.BlueprintWrite.InsertAsync(policyId, stateName, durationMinutes, mode, eventCode, load);
            }
        }

        public async Task<long> ImportPolicyJsonAsync(int envCode, string envDisplayName, string policyJson, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(policyJson)) throw new ArgumentNullException(nameof(policyJson));

            using var doc = JsonDocument.Parse(policyJson);
            var root = doc.RootElement;

            var defName = root.GetString("defName") ?? root.GetString("definitionName") ?? root.GetString("name") ?? root.GetString("displayName");
            if (string.IsNullOrWhiteSpace(defName)) throw new InvalidOperationException("Policy JSON missing defName/definitionName/name/displayName.");

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;
            try {
                var envId = await _dal.BlueprintWrite.EnsureEnvironmentByCodeAsync(envCode, envDisplayName, load);
                _ = await _dal.BlueprintWrite.EnsureDefinitionByEnvIdAsync(envId, defName!, description: null, load);

                var policyHashmaterial = root.BuildPolicyHashMaterial(); //take only relevant blocks.
                var hash = policyHashmaterial.CreateGUID(HashMethod.Sha256).ToString();
                var policyId = await _dal.BlueprintWrite.EnsurePolicyByHashAsync(hash, policyHashmaterial, load); //We can also store the actual json as is but it might contain irrelevant data which might confuse.. So, we just take what is needed and relevant for us.

                await ImportPolicyTimeoutsAsync(policyId, root, load);
                //When we do like above, we lose only important data, which is the association of policy to definition. But it is fine, because, we only need to know what is the policy.

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
                var cat = s.GetString("category");
                if (string.IsNullOrWhiteSpace(cat)) continue;

                var key = cat.N();
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
                var code = e.GetInt("code") ?? 0;
                var name = e.GetString("name") ?? e.GetString("displayName");
                if (code <= 0 || string.IsNullOrWhiteSpace(name)) continue;
                if (byCode.ContainsKey(code)) throw new InvalidOperationException($"Duplicate event code in JSON: {code}");

                var id = await _dal.BlueprintWrite.InsertEventAsync(defVersionId, name!, code, load);
                byCode[code] = new EventDef { Id = id, Code = code, Name = name!.N(), DisplayName = name! };
            }

            return byCode;
        }

        private async Task<Dictionary<string, StateDef>> ImportStatesAsync(long defVersionId, JsonElement root, Dictionary<string, int> categoryMap, Dictionary<int, EventDef> eventsByCode, DbExecutionLoad load) {
            var map = new Dictionary<string, StateDef>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("states", out var states) || states.ValueKind != JsonValueKind.Array) return map;

            foreach (var s in states.EnumerateArray()) {
                var name = s.GetString("name") ?? s.GetString("displayName");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var key = name!.N();
                if (map.ContainsKey(key)) throw new InvalidOperationException($"Duplicate state name in JSON: {name}");

                var flags = (uint)(s.GetInt("flags") ?? 0);
                if (s.GetBool("is_initial") == true) flags |= (uint)LifeCycleStateFlag.IsInitial;
                if (s.GetBool("is_final") == true) flags |= (uint)LifeCycleStateFlag.IsFinal;


                var catName = s.GetString("category");
                var catId = (!string.IsNullOrWhiteSpace(catName) && categoryMap.TryGetValue(catName!.N(), out var cid)) ? cid : 0;

                var id = await _dal.BlueprintWrite.InsertStateAsync(defVersionId, catId, name!, flags, load);

                map[key] = new StateDef {
                    Id = id,
                    Name = key,
                    DisplayName = name!,
                    Flags = flags,
                    IsInitial = (flags & (uint)LifeCycleStateFlag.IsInitial) != 0
                };
            }

            return map;
        }

        private async Task ImportTransitionsAsync(long defVersionId, JsonElement root, Dictionary<string, StateDef> statesByName, Dictionary<int, EventDef> eventsByCode, DbExecutionLoad load) {
            if (!root.TryGetProperty("transitions", out var trans) || trans.ValueKind != JsonValueKind.Array) return;

            foreach (var t in trans.EnumerateArray()) {
                var fromName = t.GetString("from") ?? t.GetString("fromState");
                var toName = t.GetString("to") ?? t.GetString("toState");
                var evCode = t.GetInt("event") ?? t.GetInt("eventCode");
                if (string.IsNullOrWhiteSpace(fromName) || string.IsNullOrWhiteSpace(toName) || !evCode.HasValue) continue;

                if (!statesByName.TryGetValue(fromName!.N(), out var from)) throw new InvalidOperationException($"Transition from-state not found: {fromName}");
                if (!statesByName.TryGetValue(toName!.N(), out var to)) throw new InvalidOperationException($"Transition to-state not found: {toName}");
                if (!eventsByCode.TryGetValue(evCode.Value, out var ev)) throw new InvalidOperationException($"Transition event code not found: {evCode}");

                var flags = (uint)(t.GetInt("flags") ?? 0);
                await _dal.BlueprintWrite.InsertTransitionAsync(defVersionId, from.Id, to.Id, ev.Id, load);
            }
        }

        static int? ParseTimeoutMinutes(JsonElement stateNode) {
            var tm = stateNode.GetInt("timeout_minutes") ?? stateNode.GetInt("timeoutMinutes");
            if (tm.HasValue) return tm;

            var dur = stateNode.GetString("timeout");
            if (string.IsNullOrWhiteSpace(dur)) return null;

            //We can use XMLConvert, but it cannot handle all cases like months and years properly.
            //var ts = XmlConvert.ToTimeSpan(dur!);
            //var mins = (int)Math.Ceiling(ts.TotalMinutes);
            if (!ISODurationUtils.TryToMinutes(dur!, out var mins) || mins <=0) return null;
            return mins;
        }

        static int ParseTimeoutMode(JsonElement stateNode) {
            var n = stateNode.GetInt("timeout_mode") ?? stateNode.GetInt("timeoutMode");
            if (n.HasValue) return n.Value;

            var s = stateNode.GetString("timeout_mode") ?? stateNode.GetString("timeoutMode");
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return string.Equals(s.Trim(), "repeat", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        static int? ParseTimeoutEventCode(JsonElement t) {
            if (!t.TryGetProperty("timeout_event", out var e)) return null;
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var c)) return c;
            if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cs)) return cs;
            return null;
        }

    }
}
