using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class StateMachineMariaRepo {

        // ----------------------------------------------------------
        // INSTANCE MANAGEMENT
        // ----------------------------------------------------------

        public async Task<IFeedback<long>> RegisterInstance(long defVersion, int currentState, int lastEvent, string externalRef, string externalType, LifeCycleInstanceFlag flags) {
            var fb = new Feedback<long>();
            try {
                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_INSTANCE.INSERT },
                    (EVENT, lastEvent),
                    (CURRENT_STATE, currentState),
                    (EXTERNAL_REF, externalRef),
                    (EXTERNAL_TYPE, externalType ?? string.Empty),
                    (FLAGS, (int)flags),
                    (DEF_VERSION, defVersion));

                if (result == null || !long.TryParse(result.ToString(), out var id))
                    return fb.SetMessage($"Unable to register instance for {externalRef}");

                return fb.SetStatus(true).SetResult(id);
            } catch (Exception ex) {
                _logger?.LogError(ex, "RegisterInstance failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetInstanceById(long id) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_INSTANCE.GET_BY_ID, Filter = ResultFilter.FirstDictionary },
                    (ID, id));

                if (result is not Dictionary<string, object> dic)
                    return fb.SetMessage($"Instance {id} not found.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetInstanceById failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByRef(string externalRef) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_INSTANCE.GET_BY_REF },
                    (EXTERNAL_REF, externalRef));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No instances found for reference '{externalRef}'.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetInstancesByRef failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByState(int stateId) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_INSTANCE.GET_BY_STATE },
                    (CURRENT_STATE, stateId));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No instances found for state {stateId}.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetInstancesByState failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByFlags(LifeCycleInstanceFlag flags) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_INSTANCE.GET_BY_FLAGS },
                    (FLAGS, (int)flags));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No instances found matching flags: {flags}.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetInstancesByFlags failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> UpdateInstanceState(long instanceId, int newState, int lastEvent, LifeCycleInstanceFlag flags) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_INSTANCE.UPDATE_STATE },
                    (CURRENT_STATE, newState),
                    (EVENT, lastEvent),
                    (FLAGS, (int)flags),
                    (ID, instanceId));

                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "UpdateInstanceState failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> MarkInstanceCompleted(long instanceId) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_INSTANCE.MARK_COMPLETED },
                    (ID, instanceId));

                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "MarkInstanceCompleted failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> DeleteInstance(long instanceId) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_INSTANCE.DELETE },
                    (ID, instanceId));

                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "DeleteInstance failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
