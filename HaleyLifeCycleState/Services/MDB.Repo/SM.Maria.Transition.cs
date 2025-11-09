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
        // TRANSITION MANAGEMENT
        // ----------------------------------------------------------

        public async Task<IFeedback<long>> RegisterTransition(int fromState, int toState, int eventId, int defVersion, LifeCycleTransitionFlag flags, string guardCondition = null) {
            var fb = new Feedback<long>();
            try {
                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_TRANSITION.INSERT },
                    (FROM_STATE, fromState),
                    (TO_STATE, toState),
                    (EVENT, eventId),
                    (FLAGS, (int)flags),
                    (GUARD_CONDITION, guardCondition ?? string.Empty),
                    (DEF_VERSION, defVersion));

                if (result == null || !long.TryParse(result.ToString(), out var id))
                    return fb.SetMessage($"Unable to register transition: {fromState} -> {toState} (event: {eventId})");

                return fb.SetStatus(true).SetResult(id);
            } catch (Exception ex) {
                _logger?.LogError(ex, "RegisterTransition failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetTransitionsByVersion(int defVersion) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_TRANSITION.GET_BY_VERSION },
                    (DEF_VERSION, defVersion));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No transitions found for version {defVersion}.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetTransitionsByVersion failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetTransition(int fromState, int eventId, int defVersion) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_TRANSITION.GET_TRANSITION, Filter = ResultFilter.FirstDictionary },
                    (FROM_STATE, fromState),
                    (EVENT, eventId),
                    (DEF_VERSION, defVersion));

                if (result is not Dictionary<string, object> dic)
                    return fb.SetMessage($"Transition not found for state {fromState} with event {eventId}.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetTransition failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetOutgoingTransitions(int fromState, int defVersion) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_TRANSITION.GET_OUTGOING },
                    (FROM_STATE, fromState),
                    (DEF_VERSION, defVersion));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No outgoing transitions found for state {fromState} (version {defVersion}).");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetOutgoingTransitions failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> DeleteTransition(int transitionId) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_TRANSITION.DELETE },
                    (ID, transitionId));

                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "DeleteTransition failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
