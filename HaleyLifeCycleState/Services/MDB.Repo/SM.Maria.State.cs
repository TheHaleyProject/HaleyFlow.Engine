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
        // STATE MANAGEMENT
        // ----------------------------------------------------------

        public async Task<IFeedback<long>> RegisterState(string displayName, int defVersion, LifeCycleStateFlag flags, string category = null) {
            var fb = new Feedback<long>();
            try {
                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_STATE.INSERT },
                    (DISPLAY_NAME, displayName),
                    (FLAGS, (int)flags),
                    (CATEGORY, category ?? string.Empty),
                    (DEF_VERSION, defVersion));

                if (result == null || !long.TryParse(result.ToString(), out var id))
                    return fb.SetMessage($"Unable to register state: {displayName}");

                return fb.SetStatus(true).SetResult(id);
            } catch (Exception ex) {
                _logger?.LogError(ex, "RegisterState failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetStatesByVersion(int defVersion) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_STATE.GET_BY_VERSION }, (DEF_VERSION, defVersion));
                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No states found for version {defVersion}.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetStatesByVersion failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetStateByName(int defVersion, string name) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_STATE.GET_BY_NAME, Filter = ResultFilter.FirstDictionary },
                    (DEF_VERSION, defVersion),
                    (NAME, name.ToLower()));

                if (result is not Dictionary<string, object> dic)
                    return fb.SetMessage($"State '{name}' not found for version {defVersion}.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetStateByName failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetInitialState(int defVersion) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_STATE.GET_INITIAL, Filter = ResultFilter.FirstDictionary },
                    (DEF_VERSION, defVersion));

                if (result is not Dictionary<string, object> dic)
                    return fb.SetMessage($"Initial state not found for version {defVersion}.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetInitialState failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetFinalState(int defVersion) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_STATE.GET_FINAL, Filter = ResultFilter.FirstDictionary },
                    (DEF_VERSION, defVersion));

                if (result is not Dictionary<string, object> dic)
                    return fb.SetMessage($"Final state not found for version {defVersion}.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetFinalState failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> UpdateStateFlags(int stateId, LifeCycleStateFlag newFlags) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_STATE.UPDATE_FLAGS },
                    (FLAGS, (int)newFlags),
                    (ID, stateId));

                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "UpdateStateFlags failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> DeleteState(int stateId) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_STATE.DELETE }, (ID, stateId));
                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "DeleteState failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
