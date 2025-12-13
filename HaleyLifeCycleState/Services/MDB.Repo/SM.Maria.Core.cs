using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public async Task<IFeedback<Dictionary<string, object>>> UpsertState(string displayName, int defVersion, LifeCycleStateFlag flags, int category = 0, int? timeoutMinutes = null, int timeoutMode = 0, int timeoutEventId = 0) {
            var existing = await _agw.ReadSingleAsync(_key, QRY_STATE.GET_BY_NAME, (DEF_VERSION, defVersion), (NAME, displayName));

            if (existing.Status && existing.Result != null && existing.Result.Count > 0) {
                var id = Convert.ToInt32(existing.Result["id"]);
                var upd = await _agw.NonQueryAsync(_key, QRY_STATE.UPDATE,
                    (ID, id),
                    (DISPLAY_NAME, displayName),
                    (FLAGS, (int)flags),
                    (CATEGORY, category),
                    (TIMEOUT, DbNull(timeoutMinutes)),
                    (TIMEOUT_MODE, timeoutMode),
                    (TIMEOUT_EVENT, timeoutEventId)
                );

                if (!upd.Status) throw new ArgumentException("Failed to update existing state.");
                return await _agw.ReadSingleAsync(_key, QRY_STATE.GET_BY_ID, (ID, id));
            }

            return await _agw.ReadSingleAsync(_key, QRY_STATE.INSERT,
                (DEF_VERSION, defVersion),
                (DISPLAY_NAME, displayName),
                (FLAGS, (int)flags),
                (CATEGORY, category),
                (TIMEOUT, DbNull(timeoutMinutes)),
                (TIMEOUT_MODE, timeoutMode),
                (TIMEOUT_EVENT, timeoutEventId)
            );
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetStateByFlags(int defVersion, LifeCycleStateFlag requiredFlags) =>
            await _agw.ReadAsync(_key, QRY_STATE.GET_BY_FLAGS, (DEF_VERSION, defVersion), (FLAGS, (int)requiredFlags));

        public async Task<IFeedback<Dictionary<string, object>>> UpsertEvent(string displayName, int code, int defVersion) {
            var existing = await _agw.ReadSingleAsync(_key, QRY_EVENT.GET_BY_CODE, (DEF_VERSION, defVersion), (CODE, code));
            if (existing.Status && existing.Result != null && existing.Result.Count > 0) return existing;
            return await _agw.ReadSingleAsync(_key, QRY_EVENT.INSERT, (DEF_VERSION, defVersion), (DISPLAY_NAME, displayName), (CODE, code));
        }

        public async Task<IFeedback<Dictionary<string, object>>> UpsertTransition(int fromState, int toState, int eventId, int defVersion) {
            var existing = await _agw.ReadSingleAsync(_key, QRY_TRANSITION.GET_BY_UNQ, (DEF_VERSION, defVersion), (FROM_STATE, fromState), (TO_STATE, toState), (EVENT, eventId));
            if (existing.Status && existing.Result != null && existing.Result.Count > 0) return existing;

            return await _agw.ReadSingleAsync(_key, QRY_TRANSITION.INSERT,
                (DEF_VERSION, defVersion),
                (FROM_STATE, fromState),
                (TO_STATE, toState),
                (EVENT, eventId)
            );
        }

        public Task<IFeedback<Dictionary<string, object>>> GetTransition(int fromState, int eventId, int defVersion) =>
            _agw.ReadSingleAsync(_key, QRY_TRANSITION.GET_TRANSITION, (DEF_VERSION, defVersion), (FROM_STATE, fromState), (EVENT, eventId));

        public Task<IFeedback<List<Dictionary<string, object>>>> ListOutgoingTransition(int fromState, int defVersion) =>
            _agw.ReadAsync(_key, QRY_TRANSITION.GET_OUTGOING, (DEF_VERSION, defVersion), (FROM_STATE, fromState));
    }
}
