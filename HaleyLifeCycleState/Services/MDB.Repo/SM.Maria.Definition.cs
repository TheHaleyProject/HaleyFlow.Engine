using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class StateMachineMariaRepo {

        // ----------------------------------------------------------
        // DEFINITION & VERSION MANAGEMENT
        // ----------------------------------------------------------

        public async Task<IFeedback<long>> RegisterDefinition(string displayName, string description, int env) {
            var fb = new Feedback<long>();
            try {
                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_DEFINITION.INSERT },
                    (DISPLAY_NAME, displayName),
                    (DESCRIPTION, description),
                    (ENV, env));

                if (result == null || !long.TryParse(result.ToString(), out var id))
                    return fb.SetMessage($"Unable to register definition: {displayName}");

                return fb.SetStatus(true).SetResult(id);
            } catch (Exception ex) {
                _logger?.LogError(ex, "RegisterDefinition failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<long>> RegisterDefinitionVersion(long parentId, int version, string jsonData) {
            var fb = new Feedback<long>();
            try {
                if (string.IsNullOrWhiteSpace(jsonData))
                    return fb.SetMessage("Definition version data cannot be empty.");

                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_DEF_VERSION.INSERT },
                    (PARENT, parentId),
                    (VERSION, version),
                    (DATA, jsonData));

                if (result == null || !long.TryParse(result.ToString(), out var id))
                    return fb.SetMessage($"Unable to register version {version} for definition {parentId}");

                return fb.SetStatus(true).SetResult(id);
            } catch (Exception ex) {
                _logger?.LogError(ex, "RegisterDefinitionVersion failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetAllDefinitions() {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_DEFINITION.GET_ALL });
                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage("No definitions found.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetAllDefinitions failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetDefinitionById(long id) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var data = await _agw.Read(new AdapterArgs(_key) { Query = QRY_DEFINITION.GET_BY_ID, Filter = ResultFilter.FirstDictionary },
                    (ID, id));

                if (data is not Dictionary<string, object> dic)
                    return fb.SetMessage($"Definition {id} not found.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetDefinitionById failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetVersionsByDefinition(long definitionId) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_DEF_VERSION.GET_BY_PARENT },
                    (PARENT, definitionId));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage("No versions found for given definition.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetVersionsByDefinition failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetLatestDefinitionVersion(long definitionId) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var data = await _agw.Read(new AdapterArgs(_key) { Query = QRY_DEF_VERSION.GET_LATEST, Filter = ResultFilter.FirstDictionary },
                    (PARENT, definitionId));

                if (data is not Dictionary<string, object> dic)
                    return fb.SetMessage("No version found.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetLatestDefinitionVersion failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> DefinitionExists(string displayName, int env) {
            var fb = new Feedback<bool>();
            try {
                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_DEFINITION.GET_BY_NAME },
                    (DISPLAY_NAME, displayName.ToLower()),
                    (ENV, env));

                bool exists = result != null;
                return fb.SetStatus(true).SetResult(exists);
            } catch (Exception ex) {
                _logger?.LogError(ex, "DefinitionExists failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> UpdateDefinitionDescription(long definitionId, string newDescription) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_DEFINITION.UPDATE_DESCRIPTION },
                    (DESCRIPTION, newDescription),
                    (ID, definitionId));

                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "UpdateDefinitionDescription failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> DeleteDefinition(long definitionId) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_DEFINITION.DELETE },
                    (ID, definitionId));

                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "DeleteDefinition failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
