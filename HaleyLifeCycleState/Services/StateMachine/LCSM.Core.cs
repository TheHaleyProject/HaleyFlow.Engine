using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Utils;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        public async Task<LifeCycleInstance> GetInstanceWithTransitionAsync(LifeCycleKey instanceKey) {
            if (instanceKey.A == null || !int.TryParse(instanceKey.A.ToString(), out var definitionVersion)) throw new ArgumentException("Instance key A must be a valid integer value (definitionVersionId).");
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            var instance = await GetInstanceAsync(instanceKey);
            if (instance == null) throw new InvalidOperationException("Instance not found.");
            return instance;
        }

        public async Task<LifeCycleInstance?> GetInstanceAsync(LifeCycleKey instanceKey) {
            var fb = await Repository.Get(LifeCycleEntity.Instance, instanceKey);
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return null;
            return MapInstance(fb.Result);
        }

        public async Task<bool> InitializeAsync(LifeCycleKey instanceKey, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active) {
            var existing = await GetInstanceWithTransitionAsync(instanceKey);
            if (existing != null) return true;
            
            var input = ParseInstanceKey(instanceKey);
            var initFb = await Repository.GetStateByFlags(input.definitionVersion, LifeCycleStateFlag.IsInitial);
            EnsureSuccess(initFb, "State_GetByFlags(IsInitial)");
            var initRow = (initFb.Result != null && initFb.Result.Count > 0) ? initFb.Result[0] : null;
            if (initRow == null) throw new InvalidOperationException($"No initial state found for def_version={input.definitionVersion}.");

            var initialStateId = initRow.GetInt("id");

            //During initialization , last_event is always null
            var insFb = await Repository.UpsertInstance(input.definitionVersion, initialStateId,null, input.externalRef, flags);
            EnsureSuccess(insFb, "Instance_Upsert");
            return true;
        }

        public async Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(LifeCycleKey instanceKey, int skip = 0, int limit = 200) {
            var instance = await GetInstanceWithTransitionAsync(instanceKey);
            if (instance == null) return Array.Empty<LifeCycleTransitionLog>();

            var rowsFb = await Repository.GetTransitionLogList(new LifeCycleKey(LifeCycleKeyType.Id, instance.Id), skip, limit);
            EnsureSuccess(rowsFb, "TransitionLog_List");
            var rows = rowsFb.Result ?? new List<Dictionary<string, object>>();

            var list = new List<LifeCycleTransitionLog>(rows.Count);
            foreach (var r in rows) {
                list.Add(MapTransitionLog(r));
            }
            return list;
        }

        public async Task<bool> ForceUpdateStateAsync(LifeCycleKey instanceKey, int newStateId, string? actor = null, string? metadata = null) {

            var instance = await GetInstanceWithTransitionAsync(instanceKey);
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
