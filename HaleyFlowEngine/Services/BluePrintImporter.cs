using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using static Haley.Internal.KeyConstants;
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
    // BlueprintImporter is the "write path" for definitions and policies.
    // It parses JSON, computes a SHA-256 hash of the content, and writes to the DB atomically.
    // Hash-guarding makes it fully idempotent: importing the same JSON twice is a no-op that
    // returns the existing ID. Call both methods every application startup — they're always safe.
    //
    // Definition import creates: environment, definition, def_version, categories, events, states, transitions.
    // Policy import creates: policy (hash → content), timeouts, and attaches the policy to the definition.
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

                // Hash the raw JSON (only the definition-relevant fields) to get a stable fingerprint.
                // If a def_version with this exact hash already exists, skip all writes and return the existing ID.
                // This means re-deploying with the same definition JSON is truly zero-cost and zero-side-effect.
                var defhashMaterial = root.BuildDefinitionHashMaterial();
                var defHash = defhashMaterial.CreateGUID(HashMethod.Sha256).ToString();
                var existing = await _dal.Blueprint.GetDefVersionByParentAndHashAsync(defId, defHash, load);
                if (existing != null) {
                    tx.Commit();
                    committed = true;
                    return existing.GetLong(KEY_ID); //Definition version already exists with the same hash. Return existing version id.
                }

                // If the JSON specifies a version number, honour it — but reject it if it's lower than what
                // already exists in the DB (that would be a backwards import). If no version in JSON, auto-assign
                // the next available number. This avoids gaps or conflicts in the version sequence.
                var nextVer = await _dal.Blueprint.GetNextDefVersionNumberByEnvCodeAndDefNameAsync(envCode, defName, load) ?? 1;
                var verToUse = requestedVer > 0 ? requestedVer : nextVer; //If version is not specified in the json, then automatically, next available version is accepted.
                if (requestedVer > 0 && requestedVer < nextVer) throw new InvalidOperationException($"JSON version={requestedVer} but DB next_version={nextVer}. Import rejected. Requested version should either be equal to or greater than the next available version. Leave version empty in the json to automatically assign the version.");

                var defVersionId = await _dal.BlueprintWrite.InsertDefVersionAsync(defId, verToUse, defhashMaterial, defHash, load);

                // Import in dependency order: categories first (referenced by states), then events, then states,
                // then transitions (which reference both states and events by code/name).
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
            // Always delete existing timeout rows for this policy before re-inserting.
            // This makes policy re-import idempotent: importing the same policy JSON twice ends up with
            // exactly the timeouts defined in the JSON, no accumulation.
            // Safe because we delete BY POLICY ID — other policies with different IDs are untouched.
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

            string? defName = root.GetString("defName") ?? root.GetString("definitionName") ?? root.GetString("name") ?? root.GetString("displayName");
            if (string.IsNullOrWhiteSpace(defName) && root.TryGetProperty("for", out var forEl) && forEl.ValueKind == JsonValueKind.Object)
                defName = forEl.GetString("definition") ?? forEl.GetString("name");
            if (string.IsNullOrWhiteSpace(defName)) throw new InvalidOperationException("Policy JSON missing defName/definitionName/name/displayName/for.definition.");

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;
            try {
                var envId = await _dal.BlueprintWrite.EnsureEnvironmentByCodeAsync(envCode, envDisplayName, load);
                _ = await _dal.BlueprintWrite.EnsureDefinitionByEnvIdAsync(envId, defName!, description: null, load);

                // Build a canonical hash of only the policy-relevant fields (rules, params, timeouts).
                // The full JSON might contain display metadata or comments — we don't want those affecting the hash.
                // EnsurePolicyByHash finds or creates a policy row; the actual JSON is stored as the content.
                var policyHashmaterial = root.BuildPolicyHashMaterial(); //take only relevant blocks.
                var hash = policyHashmaterial.CreateGUID(HashMethod.Sha256).ToString();
                var policyId = await _dal.BlueprintWrite.EnsurePolicyByHashAsync(hash, policyHashmaterial, load); //We can also store the actual json as is but it might contain irrelevant data which might confuse.. So, we just take what is needed and relevant for us.

                await ImportPolicyTimeoutsAsync(policyId, root, load);
                //When we do like above, we lose only important data, which is the association of policy to definition. But it is fine, because, we only need to know what is the policy.

                await _dal.BlueprintWrite.AttachPolicyToDefinitionByEnvCodeAndDefNameAsync(envCode, defName!, policyId, load);
                // Attaching links this policy to the definition as the "current" policy.
                // New instances created after this call will use this policy.
                // Existing in-flight instances keep whatever policy was attached at their creation time.

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

                // States carry flags for special lifecycle roles: IsInitial marks the starting state,
                // IsFinal marks terminal states. The state machine enforces exactly one initial state per version.
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

            // We support both raw minutes (timeout_minutes: 2880) and ISO 8601 duration strings (timeout: "P2D").
            // ISO 8601 is more human-readable in JSON (P2D = 2 days, PT30M = 30 minutes).
            // XmlConvert.ToTimeSpan exists but doesn't handle months/years correctly, so we use ISODurationUtils.
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


