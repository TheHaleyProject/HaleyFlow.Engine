using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaBlueprintReadDAL : MariaDALBase, IBlueprintReadDAL {
        public MariaBlueprintReadDAL(IWorkFlowDALUtil db) : base(db) { }

        public async Task<DbRow?> GetLatestDefVersionAsync(int envCode, string defName, DbExecutionLoad load = default) {
            // envCode -> environment.id
            var env = await Db.RowAsync(QRY_ENVIRONMENT.GET_BY_CODE, load, (CODE, envCode));
            if (env is null) return null;

            // definition by envId + name (name is generated from display_name, so caller must pass normalized name)
            var def = await Db.RowAsync(QRY_DEFINITION.GET_BY_PARENT_AND_NAME, load,
                (PARENT_ID, env["id"]),
                (NAME, defName)
            );
            if (def is null) return null;

            // latest def_version by definition id
            return await Db.RowAsync(QRY_DEFVERSION.GET_LATEST_BY_PARENT, load, (PARENT_ID, def["id"]));
        }

        public Task<DbRow?> GetDefVersionByIdAsync(long defVersionId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_DEFVERSION.GET_BY_ID, load, (ID, defVersionId));

        public Task<DbRows> ListStatesAsync(long defVersionId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_STATE.LIST_BY_PARENT, load, (PARENT_ID, defVersionId));

        public Task<DbRows> ListEventsAsync(long defVersionId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_EVENTS.LIST_BY_PARENT, load, (PARENT_ID, defVersionId));

        public Task<DbRows> ListTransitionsAsync(long defVersionId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_TRANSITION.LIST_BY_PARENT, load, (PARENT_ID, defVersionId));

        public Task<DbRow?> GetPolicyByIdAsync(long policyId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_POLICY.GET_BY_ID, load, (ID, policyId));

        public Task<DbRow?> GetPolicyByHashAsync(string policyHash, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_POLICY.GET_BY_HASH, load, (HASH, policyHash));

        public async Task<DbRow?> GetPolicyForStateAsync(long defId, int stateId, DbExecutionLoad load = default) {
            // Schema has no direct state->policy FK.
            // We resolve by: state.name + def_policies(policy.content contains routes[*].state == state.name)
            var st = await Db.RowAsync(QRY_STATE.GET_BY_ID, load, (ID, stateId));
            if (st is null) return null;

            var stateName = st.TryGetValue("name", out var n) ? n?.ToString() : null;
            if (string.IsNullOrWhiteSpace(stateName)) return null;

            var policies = await Db.RowsAsync(QRY_POLICY.LIST_BY_DEFINITION, load, (PARENT_ID, defId));
            if (policies.Count == 0) return null;

            // Keep DAL “dumb”: simple content match (no business logic).
            // If you want a DB-native JSON_SEARCH query later, we can add it to QRY_POLICY.
            var needle = $"\"state\": \"{stateName}\"";
            foreach (var p in policies) {
                if (!p.TryGetValue("content", out var c) || c is null) continue;
                var s = c.ToString();
                if (!string.IsNullOrEmpty(s) && s.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return null;
        }
    }
}
