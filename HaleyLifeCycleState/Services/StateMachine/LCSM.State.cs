using Haley.Enums;
using Haley.Models;
using System;
using Haley.Utils;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        public async Task<bool> IsInitialStateAsync(int definitionVersion, int stateId) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (stateId <= 0) throw new ArgumentOutOfRangeException(nameof(stateId));

            var fb = await Repository.Get(LifeCycleEntity.State, new LifeCycleKey(LifeCycleKeyType.Id, stateId));
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return false;
            var flags = (LifeCycleStateFlag)fb.Result.GetInt("flags");
            return flags.HasFlag(LifeCycleStateFlag.IsInitial);
        }

        public async Task<bool> IsFinalStateAsync(int definitionVersion, int stateId) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (stateId <= 0) throw new ArgumentOutOfRangeException(nameof(stateId));

            var fb = await Repository.Get(LifeCycleEntity.State, new LifeCycleKey(LifeCycleKeyType.Id, stateId));
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return false;
            var flags = (LifeCycleStateFlag)fb.Result.GetInt("flags");
            return flags.HasFlag(LifeCycleStateFlag.IsFinal);
        }

        public async Task<LifeCycleState> GetCurrentStateAsync(LifeCycleKey instanceKey) {
            var instance = await GetInstanceWithTransitionAsync(instanceKey);
            if (instance == null) throw new InvalidOperationException("Instance not found.");
            var stFb = await Repository.Get(LifeCycleEntity.State, new LifeCycleKey(LifeCycleKeyType.Id, instance.CurrentState));
            EnsureSuccess(stFb, "Get(State)");
            if (stFb.Result == null || stFb.Result.Count == 0) throw new InvalidOperationException($"State id={instance.CurrentState} not found.");
            return MapState(stFb.Result);
        }
    }
}
