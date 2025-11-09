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
        // TRANSITION LOG / AUDIT
        // ----------------------------------------------------------

        public async Task<IFeedback<long>> LogTransition(long instanceId, int fromState, int toState, int eventId, string actor, LifeCycleTransitionLogFlag flags, string metadata = null) {
            var fb = new Feedback<long>();
            try {
                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_TRANSITION_LOG.INSERT },
                    (INSTANCE_ID, instanceId),
                    (FROM_STATE, fromState),
                    (TO_STATE, toState),
                    (EVENT, eventId),
                    (ACTOR, actor ?? string.Empty),
                    (FLAGS, (int)flags),
                    (METADATA, metadata ?? string.Empty));

                if (result == null || !long.TryParse(result.ToString(), out var id))
                    return fb.SetMessage("Unable to log transition.");

                return fb.SetStatus(true).SetResult(id);
            } catch (Exception ex) {
                _logger?.LogError(ex, "LogTransition failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByInstance(long instanceId) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_TRANSITION_LOG.GET_BY_INSTANCE },
                    (INSTANCE_ID, instanceId));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No logs found for instance {instanceId}.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetLogsByInstance failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByStateChange(int fromState, int toState) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_TRANSITION_LOG.GET_BY_STATE_CHANGE },
                    (FROM_STATE, fromState),
                    (TO_STATE, toState));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No logs found for transition {fromState}->{toState}.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetLogsByStateChange failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByDateRange(DateTime from, DateTime to) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_TRANSITION_LOG.GET_BY_DATE_RANGE },
                    (CREATED, from),
                    (MODIFIED, to));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage("No logs found in the given date range.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetLogsByDateRange failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetLatestLogForInstance(long instanceId) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_TRANSITION_LOG.GET_LATEST_FOR_INSTANCE, Filter = ResultFilter.FirstDictionary },
                    (INSTANCE_ID, instanceId));

                if (result is not Dictionary<string, object> dic)
                    return fb.SetMessage($"No latest log found for instance {instanceId}.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetLatestLogForInstance failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
