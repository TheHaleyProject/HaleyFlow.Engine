using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Utils;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        public async Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, LifeCycleKey instanceKey) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            var key = ToRepoInstanceKey(definitionVersion, instanceKey);

            var fb = await Repository.Get(LifeCycleEntity.Instance, key);
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return null;
            return MapInstance(fb.Result);
        }

        public async Task<bool> InitializeAsync(int definitionVersion, LifeCycleKey instanceKey, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));

            var existing = await GetInstanceAsync(definitionVersion, instanceKey);
            if (existing != null) return true;

            var initFb = await Repository.GetStateByFlags(definitionVersion, LifeCycleStateFlag.IsInitial);
            EnsureSuccess(initFb, "State_GetByFlags(IsInitial)");
            var initRow = (initFb.Result != null && initFb.Result.Count > 0) ? initFb.Result[0] : null;
            if (initRow == null) throw new InvalidOperationException($"No initial state found for def_version={definitionVersion}.");

            var initialStateId = initRow.GetInt("id");
            var externalRef = InstanceKeyToExternalRef(instanceKey);

            var insFb = await Repository.UpsertInstance(definitionVersion, initialStateId, 0, externalRef, flags);
            EnsureSuccess(insFb, "Instance_Upsert");
            return true;
        }

        public async Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, LifeCycleKey instanceKey) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            var instance = await GetInstanceAsync(definitionVersion, instanceKey);
            if (instance == null) throw new InvalidOperationException("Instance not found.");

            var stFb = await Repository.Get(LifeCycleEntity.State, new LifeCycleKey(LifeCycleKeyType.Id, instance.CurrentState));
            EnsureSuccess(stFb, "Get(State)");
            if (stFb.Result == null || stFb.Result.Count == 0) throw new InvalidOperationException($"State id={instance.CurrentState} not found.");
            return MapState(stFb.Result);
        }

        public async Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(int definitionVersion, LifeCycleKey instanceKey, int skip = 0, int limit = 200) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));

            var instance = await GetInstanceAsync(definitionVersion, instanceKey);
            if (instance == null) return Array.Empty<LifeCycleTransitionLog>();

            var rowsFb = await Repository.GetTransitionLogList(new LifeCycleKey(LifeCycleKeyType.Id, instance.Id), skip, limit);
            EnsureSuccess(rowsFb, "TransitionLog_List");
            var rows = rowsFb.Result ?? new List<Dictionary<string, object>>();

            var list = new List<LifeCycleTransitionLog>(rows.Count);
            foreach (var r in rows) {
                list.Add(new LifeCycleTransitionLog {
                    Id = r.GetLong("id"),
                    InstanceId = r.GetLong("instance_id"),
                    FromState = r.GetInt("from_state"),
                    ToState = r.GetInt("to_state"),
                    Event = r.GetInt("event"),
                    Created = DateTime.UtcNow
                });
            }

            return list;
        }

        public async Task<bool> ForceUpdateStateAsync(int definitionVersion, LifeCycleKey instanceKey, int newStateId, string? actor = null, string? metadata = null) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (newStateId <= 0) throw new ArgumentOutOfRangeException(nameof(newStateId));

            var instance = await GetInstanceAsync(definitionVersion, instanceKey);
            if (instance == null) throw new InvalidOperationException("Instance not found.");

            var actorValue = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
            var meta = metadata ?? string.Empty;

            var logIdFb = await Repository.AppendTransitionLog(instance.Id, instance.CurrentState, newStateId, 0, actorValue, meta);
            EnsureSuccess(logIdFb, "TransitionLog_Append");

            var updFb = await Repository.UpdateInstanceState(new LifeCycleKey(LifeCycleKeyType.Id, instance.Id), newStateId, 0, instance.Flags);
            EnsureSuccess(updFb, "Instance_UpdateState");
            return updFb.Result;
        }
    }
}
