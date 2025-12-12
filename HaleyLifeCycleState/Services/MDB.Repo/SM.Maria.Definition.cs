using Haley.Abstractions;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public Task<IFeedback<Dictionary<string, object>>> RegisterDefinition(string displayName, string description, int env) =>
            _agw.ReadSingleAsync(_key, QRY_DEFINITION.INSERT, (DISPLAY_NAME, displayName), (DESCRIPTION, description), (ENV, env));

        public Task<IFeedback<Dictionary<string, object>>> RegisterDefinitionVersion(long parentId, int version, string jsonData) =>
            _agw.ReadSingleAsync(_key, QRY_DEF_VERSION.INSERT, (PARENT, parentId), (VERSION, version), (DATA, jsonData));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetAllDefinitions() =>
            _agw.ReadAsync(_key, QRY_DEFINITION.GET_ALL);

        public Task<IFeedback<Dictionary<string, object>>> GetDefinitionById(long id) =>
            _agw.ReadSingleAsync(_key, QRY_DEFINITION.GET_BY_ID, (ID, id));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetVersionsByDefinition(long definitionId) =>
            _agw.ReadAsync(_key, QRY_DEF_VERSION.GET_BY_PARENT, (PARENT, definitionId));

        public Task<IFeedback<Dictionary<string, object>>> GetLatestDefinitionVersion(long definitionId) =>
            _agw.ReadSingleAsync(_key, QRY_DEF_VERSION.GET_LATEST, (PARENT, definitionId));

        public async Task<IFeedback<bool>> DefinitionExists(string displayName, int env) {
            var res = await _agw.ReadSingleAsync(_key, QRY_DEFINITION.GET_BY_NAME, (ENV, env), (NAME, displayName));
            return new Feedback<bool>().SetStatus(true).SetResult(res.Status && res.Result != null);
        }

        public Task<IFeedback<bool>> UpdateDefinitionDescription(long definitionId, string newDescription) =>
            _agw.NonQueryAsync(_key, QRY_DEFINITION.UPDATE_DESCRIPTION, (DESCRIPTION, newDescription), (ID, definitionId));

        public Task<IFeedback<bool>> DeleteDefinition(long definitionId) =>
            _agw.NonQueryAsync(_key, QRY_DEFINITION.DELETE, (ID, definitionId));
    }
}
