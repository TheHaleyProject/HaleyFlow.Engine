using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public async Task<IFeedback<long>> LogTransition(long instanceId, int fromState, int toState, int eventId, string actor, LifeCycleTransitionLogFlag flags, string metadata = null) {
            var fb = new Feedback<long>();

            // 1) transition_log (scalar id)
            var logRes = await _agw.ScalarAsync<long>(_key, QRY_TRANSITION_LOG.INSERT,
                (INSTANCE_ID, instanceId),
                (FROM_STATE, fromState),
                (TO_STATE, toState),
                (EVENT, eventId),
                (FLAGS, (int)flags));

            if (!logRes.Status) return fb.SetMessage(logRes.Message);

            var logId = logRes.Result;

            // 2) transition_data (actor/metadata)
            var dataRes = await _agw.NonQueryAsync(_key, QRY_TRANSITION_DATA.UPSERT,
                (TRANSITION_LOG, logId),
                (ACTOR, actor ?? string.Empty),
                (METADATA, metadata ?? string.Empty));

            if (!dataRes.Status) return fb.SetMessage(dataRes.Message);

            return fb.SetStatus(true).SetResult(logId);
        }

        public Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByInstance(long instanceId) =>
            _agw.ReadAsync(_key, QRY_TRANSITION_LOG.GET_BY_INSTANCE, (INSTANCE_ID, instanceId));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByStateChange(int fromState, int toState) =>
            _agw.ReadAsync(_key, QRY_TRANSITION_LOG.GET_BY_STATE_CHANGE, (FROM_STATE, fromState), (TO_STATE, toState));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByDateRange(DateTime from, DateTime to) =>
            _agw.ReadAsync(_key, QRY_TRANSITION_LOG.GET_BY_DATE_RANGE, (CREATED, from), (MODIFIED, to));

        public Task<IFeedback<Dictionary<string, object>>> GetLatestLogForInstance(long instanceId) =>
            _agw.ReadSingleAsync(_key, QRY_TRANSITION_LOG.GET_LATEST_FOR_INSTANCE, (INSTANCE_ID, instanceId));
    }
}
