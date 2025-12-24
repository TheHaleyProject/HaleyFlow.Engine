using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStoreMaria {

        // Base (Environment, Category, Definition, DefVersion)
        public async Task<IFeedback<Dictionary<string, object>>> UpsertEnvironment(string displayName, int code) {
            var existing = await _agw.ReadSingleAsync(_key, QRY_ENVIRONMENT.GET_BY_CODE, (CODE, code));
            if (existing.Status && existing.Result != null && existing.Result.Count > 0) {
                if (existing.Result.TryGetValue("display_name", out var dn) && !string.Equals(Convert.ToString(dn), displayName?.Trim(), StringComparison.InvariantCultureIgnoreCase)) {
                    var id = Convert.ToInt32(existing.Result["id"]);
                    await _agw.NonQueryAsync(_key, QRY_ENVIRONMENT.UPDATE_DISPLAY_NAME, (ID, id), (DISPLAY_NAME, displayName));
                    return await _agw.ReadSingleAsync(_key, QRY_ENVIRONMENT.GET_BY_CODE, (CODE, code));
                }
                return existing;
            }
            return await _agw.ReadSingleAsync(_key, QRY_ENVIRONMENT.INSERT, (DISPLAY_NAME, displayName), (CODE, code));
        }

        public async Task<IFeedback<Dictionary<string, object>>> UpsertCategory(string displayName) {
            var existing = await _agw.ReadSingleAsync(_key, QRY_CATEGORY.GET_BY_NAME, (NAME, displayName));
            if (existing.Status && existing.Result != null && existing.Result.Count > 0) return existing;
            return await _agw.ReadSingleAsync(_key, QRY_CATEGORY.INSERT, (DISPLAY_NAME, displayName));
        }

        public async Task<IFeedback<Dictionary<string, object>>> UpsertDefinition(string displayName, string description, int envId) {
            var existing = await _agw.ReadSingleAsync(_key, QRY_DEFINITION.GET_BY_NAME, (ENV, envId), (NAME, displayName));
            if (existing.Status && existing.Result != null && existing.Result.Count > 0) return existing;
            return await _agw.ReadSingleAsync(_key, QRY_DEFINITION.INSERT, (ENV, envId), (DISPLAY_NAME, displayName), (DESCRIPTION, description));
        }

        public Task<IFeedback<bool>> UpdateDefinitionDescription(long definitionId, string newDescription) =>
            _agw.NonQueryAsync(_key, QRY_DEFINITION.UPDATE_DESCRIPTION, (ID, definitionId), (DESCRIPTION, newDescription));

        public async Task<IFeedback<Dictionary<string, object>>> UpsertDefinitionVersion(long parentDefinitionId, int version, string jsonData) {
            var existing = await _agw.ReadSingleAsync(_key, QRY_DEF_VERSION.GET_BY_PARENT_VERSION, (PARENT, parentDefinitionId), (VERSION, version));

            if (existing.Status && existing.Result != null && existing.Result.Count > 0) {
                //Exists, so we just update the data alone.
                var id = Convert.ToInt32(existing.Result["id"]);
                var upd = await _agw.NonQueryAsync(_key, QRY_DEF_VERSION.UPDATE_DATA, (ID, id), (DATA, jsonData));
                if (!upd.Status) throw new ArgumentException("Failed to update existing definition version data.");
                return await _agw.ReadSingleAsync(_key, QRY_DEF_VERSION.GET_BY_ID, (ID, id));
            }

            return await _agw.ReadSingleAsync(_key, QRY_DEF_VERSION.INSERT, (PARENT, parentDefinitionId), (VERSION, version), (DATA, jsonData));
        }

        public Task<IFeedback<Dictionary<string, object>>> GetLatestDefinitionVersion(LifeCycleKey key) =>
            key.Type == WorkFlowEntityKeyType.Parent
                ? _agw.ReadSingleAsync(_key, QRY_DEF_VERSION.GET_LATEST, (PARENT, Convert.ToInt64(key.keys[0])))
                : _agw.ReadSingleAsync(_key, QRY_DEF_VERSION.GET_LATEST_BY_ENV, (CODE, Convert.ToInt32(key.keys[0])), (NAME, Convert.ToString(key.keys[1]!)!));
    }
}
