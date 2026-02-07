using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    // MariaBlueprintWriteDAL.cs
    internal sealed class MariaBlueprintWriteDAL : MariaDALBase, IBlueprintWriteDAL {
        public MariaBlueprintWriteDAL(IDALUtilBase db) : base(db) { }

        public async Task<int> EnsureEnvironmentByCodeAsync(int envCode, string envDisplayName, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_ENVIRONMENT.EXISTS_BY_CODE, load, (CODE, envCode));
            if (exists.HasValue) {
                var row = await Db.RowAsync(QRY_ENVIRONMENT.GET_BY_CODE, load, (CODE, envCode));
                if (row == null) throw new InvalidOperationException($"environment not found after EXISTS. code={envCode}");
                return row.GetInt("id");
            }

            try {
                return await Db.ScalarAsync<int>(QRY_ENVIRONMENT.INSERT, load, (DISPLAY_NAME, envDisplayName), (CODE, envCode));
            } catch {
                var row = await Db.RowAsync(QRY_ENVIRONMENT.GET_BY_CODE, load, (CODE, envCode));
                if (row == null) throw;
                return row.GetInt("id");
            }
        }

        public async Task<int> EnsureDefinitionByEnvIdAsync(int envId, string defDisplayName, string? description, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_DEFINITION.EXISTS_BY_PARENT_AND_NAME, load, (PARENT_ID, envId), (NAME, defDisplayName));
            if (exists.HasValue) {
                var row = await Db.RowAsync(QRY_DEFINITION.GET_BY_PARENT_AND_NAME, load, (PARENT_ID, envId), (NAME, defDisplayName));
                if (row == null) throw new InvalidOperationException($"definition not found after EXISTS. env={envId}, def={defDisplayName}");
                return row.GetInt("id");
            }

            try {
                return await Db.ScalarAsync<int>(QRY_DEFINITION.INSERT, load, (PARENT_ID, envId), (DISPLAY_NAME, defDisplayName), (DESCRIPTION, description));
            } catch {
                var row = await Db.RowAsync(QRY_DEFINITION.GET_BY_PARENT_AND_NAME, load, (PARENT_ID, envId), (NAME, defDisplayName));
                if (row == null) throw;
                return row.GetInt("id");
            }
        }

        public async Task<long> InsertDefVersionAsync(int definitionId, int version, string data, string hash, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_DEFVERSION.EXISTS_BY_PARENT_AND_VERSION, load, (PARENT_ID, definitionId), (VERSION, version));
            if (exists.HasValue) {
                var rows = await Db.RowsAsync(QRY_DEFVERSION.LIST_BY_PARENT, load, (PARENT_ID, definitionId));
                foreach (var r in rows) if (r.GetInt("version") == version) return r.GetLong("id");
                throw new InvalidOperationException($"def_version exists but id not resolvable. def={definitionId}, ver={version}");
            }

            try {
                return await Db.ScalarAsync<long>(QRY_DEFVERSION.INSERT, load, (PARENT_ID, definitionId), (VERSION, version), (DATA, data),(HASH,hash));
            } catch {
                var rows = await Db.RowsAsync(QRY_DEFVERSION.LIST_BY_PARENT, load, (PARENT_ID, definitionId));
                foreach (var r in rows) if (r.GetInt("version") == version) return r.GetLong("id");
                throw;
            }
        }

        public async Task<int> EnsureCategoryByNameAsync(string displayName, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_CATEGORY.EXISTS_BY_NAME, load, (NAME, displayName));
            if (exists.HasValue) {
                var row = await Db.RowAsync(QRY_CATEGORY.GET_BY_NAME, load, (NAME, displayName));
                if (row == null) throw new InvalidOperationException($"category not found after EXISTS. name={displayName}");
                return row.GetInt("id");
            }

            try {
                return await Db.ScalarAsync<int>(QRY_CATEGORY.INSERT, load, (DISPLAY_NAME, displayName));
            } catch {
                var row = await Db.RowAsync(QRY_CATEGORY.GET_BY_NAME, load, (NAME, displayName));
                if (row == null) throw;
                return row.GetInt("id");
            }
        }

        public async Task<int> InsertEventAsync(long defVersionId, string displayName, int code, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_EVENTS.EXISTS_BY_PARENT_AND_CODE, load, (PARENT_ID, defVersionId), (CODE, code));
            if (exists.HasValue) {
                var row = await Db.RowAsync(QRY_EVENTS.GET_BY_PARENT_AND_CODE, load, (PARENT_ID, defVersionId), (CODE, code));
                if (row == null) throw new InvalidOperationException($"event not found after EXISTS. dv={defVersionId}, code={code}");
                return row.GetInt("id");
            }

            try {
                return await Db.ScalarAsync<int>(QRY_EVENTS.INSERT, load, (PARENT_ID, defVersionId), (DISPLAY_NAME, displayName), (CODE, code));
            } catch {
                var row = await Db.RowAsync(QRY_EVENTS.GET_BY_PARENT_AND_CODE, load, (PARENT_ID, defVersionId), (CODE, code));
                if (row == null) throw;
                return row.GetInt("id");
            }
        }

        public async Task<int> InsertStateAsync(long defVersionId, int categoryId, string displayName, uint flags, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_STATE.EXISTS_BY_PARENT_AND_NAME, load, (PARENT_ID, defVersionId), (NAME, displayName));
            if (exists.HasValue) {
                var row = await Db.RowAsync(QRY_STATE.GET_BY_PARENT_AND_NAME, load, (PARENT_ID, defVersionId), (NAME, displayName));
                if (row == null) throw new InvalidOperationException($"State not found after EXISTS. dv={defVersionId}, name={displayName}");
                return row.GetInt("id");
            }

            try {
                return await Db.ScalarAsync<int>(QRY_STATE.INSERT, load,(PARENT_ID, defVersionId), (DISPLAY_NAME, displayName), (CATEGORY_ID, categoryId),(FLAGS, flags));
            } catch {
                var row = await Db.RowAsync(QRY_STATE.GET_BY_PARENT_AND_NAME, load, (PARENT_ID, defVersionId), (NAME, displayName));
                if (row == null) throw;
                return row.GetInt("id");
            }
        }

        public async Task<int> InsertTransitionAsync(long defVersionId, int fromStateId, int toStateId, int eventId, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_TRANSITION.EXISTS_BY_KEY, load, (PARENT_ID, defVersionId), (FROM_ID, fromStateId), (TO_ID, toStateId), (EVENT_ID, eventId));
            if (exists.HasValue) {
                var row = await Db.RowAsync(QRY_TRANSITION.GET_BY_KEY, load, (PARENT_ID, defVersionId), (FROM_ID, fromStateId), (TO_ID, toStateId), (EVENT_ID, eventId));
                if (row == null) throw new InvalidOperationException($"transition not found after EXISTS. dv={defVersionId}, from={fromStateId}, to={toStateId}, ev={eventId}");
                return row.GetInt("id");
            }

            try {
                return await Db.ScalarAsync<int>(QRY_TRANSITION.INSERT, load, (PARENT_ID, defVersionId), (FROM_ID, fromStateId), (TO_ID, toStateId), (EVENT_ID, eventId));
            } catch {
                var row = await Db.RowAsync(QRY_TRANSITION.GET_BY_KEY, load, (PARENT_ID, defVersionId), (FROM_ID, fromStateId), (TO_ID, toStateId), (EVENT_ID, eventId));
                if (row == null) throw;
                return row.GetInt("id");
            }
        }

        public async Task<long> EnsurePolicyByHashAsync(string hash, string content, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_POLICY.EXISTS_BY_HASH, load, (HASH, hash));
            if (exists.HasValue) {
                var row = await Db.RowAsync(QRY_POLICY.GET_BY_HASH, load, (HASH, hash));
                if (row == null) throw new InvalidOperationException($"policy not found after EXISTS. hash={hash}");
                var id = row.GetLong("id");
                //At this point, it is more than enough to return the id.. But, we are also comparing if the content is same.. if not, we update it. todo: check if we really need to do this or not..
                var old = row.GetString("content");
                if (!string.Equals(old, content, StringComparison.Ordinal)) await Db.ExecAsync(QRY_POLICY.UPDATE_CONTENT, load, (ID, id), (CONTENT, content));
                return id;
            }

            try {
                return await Db.ScalarAsync<long>(QRY_POLICY.INSERT, load, (HASH, hash), (CONTENT, content));
            } catch {
                var row = await Db.RowAsync(QRY_POLICY.GET_BY_HASH, load, (HASH, hash));
                if (row == null) throw;
                return row.GetLong("id");
            }
        }

        public Task<int> AttachPolicyToDefinitionByEnvCodeAndDefNameAsync(int envCode, string defName, long policyId, DbExecutionLoad load = default) => Db.ExecAsync(QRY_POLICY.ATTACH_TO_DEFINITION_BY_ENV_CODE_AND_DEF_NAME, load, (CODE, envCode), (DEF_NAME, defName), (ID, policyId));

        public Task<int> DeleteByPolicyIdAsync(long policyId, DbExecutionLoad load = default) => Db.ExecAsync(QRY_TIMEOUTS.DELETE_BY_POLICY_ID, load, (POLICY_ID, policyId));

        public Task<int> InsertAsync(long policyId, string stateName, int duration, int mode, int? eventCode, DbExecutionLoad load = default) => Db.ExecAsync(QRY_TIMEOUTS.INSERT, load, (POLICY_ID, policyId), (STATE_NAME, stateName), (DURATION, duration), (MODE, mode), (EVENT_CODE, eventCode));

        public Task<DbRows> ListByPolicyIdAsync(long policyId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_TIMEOUTS.LIST_BY_POLICY_ID, load, (POLICY_ID, policyId));
    }

}
