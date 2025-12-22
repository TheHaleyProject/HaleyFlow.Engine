using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public async Task<IFeedback<Dictionary<string, object>>> UpsertInstance(int defVersion, int currentState, int? lastEvent, string externalRef, LifeCycleInstanceFlag flags) {
            var existing = await _agw.ReadSingleAsync(_key, QRY_INSTANCE.GET_BY_REF, (DEF_VERSION, defVersion), (EXTERNAL_REF, externalRef));
            if (existing.Status && existing.Result != null && existing.Result.Count > 0) return existing;
            return await _agw.ReadSingleAsync(_key, QRY_INSTANCE.INSERT,
                (DEF_VERSION, defVersion),
                (CURRENT_STATE, currentState),
                (EVENT, AssertNull(lastEvent)), //For initialization, lastevent could be null as this is the starting event.
                (EXTERNAL_REF, externalRef),
                (FLAGS, (int)flags)
            );
        }

        public Task<IFeedback<bool>> UpdateInstanceState(LifeCycleKey key, int newState, int lastEvent, LifeCycleInstanceFlag flags) =>
            key.Type == LifeCycleKeyType.Guid
                ? _agw.NonQueryAsync(_key, QRY_INSTANCE.UPDATE_STATE_BY_GUID, (GUID, key.keys[0]), (CURRENT_STATE, newState), (EVENT, lastEvent), (FLAGS, (int)flags))
                : _agw.NonQueryAsync(_key, QRY_INSTANCE.UPDATE_STATE, (ID, Convert.ToInt64(key.keys[0])), (CURRENT_STATE, newState), (EVENT, lastEvent), (FLAGS, (int)flags));

        public Task<IFeedback<bool>> MarkInstanceCompleted(LifeCycleKey key) =>
            key.Type == LifeCycleKeyType.Guid
                ? _agw.NonQueryAsync(_key, QRY_INSTANCE.MARK_COMPLETED_BY_GUID, (GUID, key.keys[0]))
                : _agw.NonQueryAsync(_key, QRY_INSTANCE.MARK_COMPLETED, (ID, Convert.ToInt64(key.keys[0])));

        public async Task<IFeedback<long>> AppendTransitionLog(long instanceId, int fromState, int toState, int eventId, string? actor = null, string? metadata = null) {
            var fb = new Feedback<long>();

            var ins = await _agw.ReadSingleAsync(_key, QRY_TRANSITION_LOG.INSERT, (INSTANCE_ID, instanceId), (FROM_STATE, fromState), (TO_STATE, toState), (EVENT, eventId));
            if (!ins.Status || ins.Result == null || ins.Result.Count == 0) return fb.SetStatus(false).SetMessage(ins.Message);

            long logId = 0;
            foreach (var v in ins.Result.Values) { logId = Convert.ToInt64(v); break; }
            if (logId <= 0) return fb.SetStatus(false).SetMessage("Failed to read log id.");

            if (!string.IsNullOrWhiteSpace(actor) || !string.IsNullOrWhiteSpace(metadata)) {
                var dataRes = await _agw.NonQueryAsync(_key, QRY_TRANSITION_DATA.UPSERT, (TRANSITION_LOG, logId), (ACTOR, AssertNull(actor)), (METADATA, AssertNull(metadata)));
                if (!dataRes.Status) return fb.SetStatus(false).SetMessage(dataRes.Message);
            }

            return fb.SetStatus(true).SetResult(logId);
        }

        public Task<IFeedback<Dictionary<string, object>>> GetTransitionLog(LifeCycleKey key) =>
            _agw.ReadSingleAsync(_key, QRY_TRANSITION_LOG.GET_BY_ID, (ID, Convert.ToInt64(key.keys[0])));

        public Task<IFeedback<Dictionary<string, object>>> GetLatestTransitionLog(long instanceId) =>
            _agw.ReadSingleAsync(_key, QRY_TRANSITION_LOG.GET_LATEST_FOR_INSTANCE, (INSTANCE_ID, instanceId));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetTransitionLogList(LifeCycleKey key, int skip = 0, int limit = 200) {
            var sql = key.Type switch {
                LifeCycleKeyType.Id => QRY_TRANSITION_LOG.GET_BY_INSTANCE,
                LifeCycleKeyType.Composite when key.keys[0] is object[] a && a.Length >= 2 && (a[0] is DateTime || a[1] is DateTime) => QRY_TRANSITION_LOG.GET_BY_DATE_RANGE,
                LifeCycleKeyType.Composite when key.keys[0] is object[] b && b.Length >= 2 => QRY_TRANSITION_LOG.GET_BY_STATE_CHANGE,
                _ => throw new NotSupportedException("Invalid TransitionLog_List filter.")
            };

            if (!sql.Contains(LIMIT, StringComparison.OrdinalIgnoreCase)) sql = sql.TrimEnd().TrimEnd(';') + $" LIMIT {SKIP}, {LIMIT};";

            return key.Type switch {
                LifeCycleKeyType.Id => _agw.ReadAsync(_key, sql, (INSTANCE_ID, Convert.ToInt64(key.keys[0])), (SKIP, skip), (LIMIT, limit)),
                LifeCycleKeyType.Composite when key.keys[0] is object[] a && a.Length >= 2 && (a[0] is DateTime || a[1] is DateTime) => _agw.ReadAsync(_key, sql, (CREATED, a[0]), (MODIFIED, a[1]), (SKIP, skip), (LIMIT, limit)),
                LifeCycleKeyType.Composite when key.keys[0] is object[] b && b.Length >= 2 => _agw.ReadAsync(_key, sql, (FROM_STATE, Convert.ToInt32(b[0])), (TO_STATE, Convert.ToInt32(b[1])), (SKIP, skip), (LIMIT, limit)),
                _ => throw new NotSupportedException("Invalid TransitionLog_List filter.")
            };
        }


    }
}
