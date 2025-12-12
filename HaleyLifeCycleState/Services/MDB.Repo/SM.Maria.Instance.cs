using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public Task<IFeedback<Dictionary<string, object>>> RegisterInstance(long defVersion, int currentState, int lastEvent, string externalRef, LifeCycleInstanceFlag flags) =>
            _agw.ReadSingleAsync(_key, QRY_INSTANCE.INSERT,
                (EVENT, lastEvent),
                (CURRENT_STATE, currentState),
                (EXTERNAL_REF, externalRef),
                (FLAGS, (int)flags),
                (DEF_VERSION, defVersion));

        public Task<IFeedback<Dictionary<string, object>>> GetInstanceById(long id) =>
            _agw.ReadSingleAsync(_key, QRY_INSTANCE.GET_BY_ID, (ID, id));

        public Task<IFeedback<Dictionary<string, object>>> GetInstanceByGuid(string guid) =>
            _agw.ReadSingleAsync(_key, QRY_INSTANCE.GET_BY_GUID, (GUID, guid));

        // All versions (ordered desc). External ref is expected to be globally unique, but schema allows multiple versions.
        public Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByRef(string externalRef) =>
            _agw.ReadAsync(_key, QRY_INSTANCE.GET_BY_REF_ANY_VERSION, (EXTERNAL_REF, externalRef));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByState(int defVersion, int stateId) =>
            _agw.ReadAsync(_key, QRY_INSTANCE.GET_BY_STATE_IN_VERSION, (DEF_VERSION, defVersion), (CURRENT_STATE, stateId));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByFlags(int defVersion, LifeCycleInstanceFlag flags) =>
            _agw.ReadAsync(_key, QRY_INSTANCE.GET_BY_FLAGS_IN_VERSION, (DEF_VERSION, defVersion), (FLAGS, (int)flags));

        public Task<IFeedback<bool>> UpdateInstanceState(long instanceId, int newState, int lastEvent, LifeCycleInstanceFlag flags) =>
            _agw.NonQueryAsync(_key, QRY_INSTANCE.UPDATE_STATE, (CURRENT_STATE, newState), (EVENT, lastEvent), (FLAGS, (int)flags), (ID, instanceId));

        public Task<IFeedback<bool>> UpdateInstanceStateByGuid(string guid, int newState, int lastEvent, LifeCycleInstanceFlag flags) =>
            _agw.NonQueryAsync(_key, QRY_INSTANCE.UPDATE_STATE_BY_GUID, (CURRENT_STATE, newState), (EVENT, lastEvent), (FLAGS, (int)flags), (GUID, guid));

        public Task<IFeedback<bool>> MarkInstanceCompleted(long instanceId) =>
            _agw.NonQueryAsync(_key, QRY_INSTANCE.MARK_COMPLETED, (ID, instanceId));

        public Task<IFeedback<bool>> MarkInstanceCompletedByGuid(string guid) =>
            _agw.NonQueryAsync(_key, QRY_INSTANCE.MARK_COMPLETED_BY_GUID, (GUID, guid));

        public Task<IFeedback<bool>> DeleteInstance(long instanceId) =>
            _agw.NonQueryAsync(_key, QRY_INSTANCE.DELETE, (ID, instanceId));

        public Task<IFeedback<bool>> DeleteInstanceByGuid(string guid) =>
            _agw.NonQueryAsync(_key, QRY_INSTANCE.DELETE_BY_GUID, (GUID, guid));
    }
}
