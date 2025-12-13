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
    }
}
