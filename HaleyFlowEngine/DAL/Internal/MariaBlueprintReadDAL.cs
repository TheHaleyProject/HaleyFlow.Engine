using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaBlueprintReadDAL : MariaDALBase, IBlueprintReadDAL {
        public MariaBlueprintReadDAL(IDALUtilBase db) : base(db) { }

        public Task<DbRow?> GetLatestDefVersionByEnvCodeAndDefNameAsync(int envCode, string defName, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_DEFVERSION.GET_LATEST_BY_ENV_CODE_AND_DEF_NAME, load, (CODE, envCode), (NAME, defName));

        public Task<DbRow?> GetLatestDefVersionByEnvNameAndDefNameAsync(string envName, string defName, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_DEFVERSION.GET_LATEST_BY_ENV_NAME_AND_DEF_NAME, load, (ENV_NAME, envName), (DEF_NAME, defName));

        public Task<DbRow?> GetLatestDefVersionByDefinitionGuidAsync(string defGuid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_DEFVERSION.GET_LATEST_BY_DEFINITION_GUID, load, (GUID, defGuid));

        public Task<DbRow?> GetLatestDefVersionByLineFromDefVersionIdAsync(long defVersionId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_DEFVERSION.GET_LATEST_BY_LINE_FROM_DEFVERSION_ID, load, (ID, defVersionId));

        public Task<DbRow?> GetLatestDefVersionByLineFromDefVersionGuidAsync(string defVersionGuid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_DEFVERSION.GET_LATEST_BY_LINE_FROM_DEFVERSION_GUID, load, (GUID, defVersionGuid));

        public async Task<int?> GetNextDefVersionNumberByEnvCodeAndDefNameAsync(int envCode, string defName, DbExecutionLoad load = default) {
            var row = await Db.RowAsync(QRY_DEFVERSION.GET_NEXT_VERSION_BY_ENV_CODE_AND_DEF_NAME, load, (CODE, envCode), (NAME, defName));
            return row.GetInt("next_version");
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

        public Task<DbRow?> GetPolicyByHashAsync(string hash, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_POLICY.GET_BY_HASH, load, (HASH, hash));

        public Task<DbRow?> GetPolicyForDefinition(long definitionId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_POLICY.GET_POLICY_FOR_DEFINITION, load, (PARENT_ID, definitionId)); // you said this is correct

        public Task<DbRow?> GetPolicyForDefVersion(long defVersionId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_POLICY.GET_POLICY_FOR_DEFVERSION, load, (ID, defVersionId)); // you said this is correct

        public Task<DbRow?> GetDefVersionByParentAndHashAsync(int definitionId, string hash, DbExecutionLoad load = default) => Db.RowAsync(QRY_DEFVERSION.GET_BY_PARENT_AND_HASH, load, (PARENT_ID,definitionId),(HASH,hash));
    }
}
