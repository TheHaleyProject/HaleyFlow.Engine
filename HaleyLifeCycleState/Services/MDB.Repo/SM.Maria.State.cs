using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public Task<IFeedback<Dictionary<string, object>>> RegisterState(string displayName, int defVersion, LifeCycleStateFlag flags, int category = 0) =>
            _agw.ReadSingleAsync(_key, QRY_STATE.INSERT, (DISPLAY_NAME, displayName), (FLAGS, (int)flags), (CATEGORY, category), (DEF_VERSION, defVersion));

        public Task<IFeedback<List<Dictionary<string, object>>>> GetStatesByVersion(int defVersion) =>
            _agw.ReadAsync(_key, QRY_STATE.GET_BY_VERSION, (DEF_VERSION, defVersion));

        public Task<IFeedback<Dictionary<string, object>>> GetStateByName(int defVersion, string name) =>
            _agw.ReadSingleAsync(_key, QRY_STATE.GET_BY_NAME, (DEF_VERSION, defVersion), (NAME, name));

        public Task<IFeedback<Dictionary<string, object>>> GetInitialState(int defVersion) =>
            _agw.ReadSingleAsync(_key, QRY_STATE.GET_INITIAL, (DEF_VERSION, defVersion));

        public Task<IFeedback<Dictionary<string, object>>> GetFinalState(int defVersion) =>
            _agw.ReadSingleAsync(_key, QRY_STATE.GET_FINAL, (DEF_VERSION, defVersion));

        public Task<IFeedback<bool>> UpdateStateFlags(int stateId, LifeCycleStateFlag newFlags) =>
            _agw.NonQueryAsync(_key, QRY_STATE.UPDATE_FLAGS, (FLAGS, (int)newFlags), (ID, stateId));

        public Task<IFeedback<bool>> DeleteState(int stateId) =>
            _agw.NonQueryAsync(_key, QRY_STATE.DELETE, (ID, stateId));
    }
}
