using Haley.Enums;
using Haley.Events;
using Haley.Models;
using Haley.Abstractions;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleProcessor {

        public async Task<LifeCycleInstance?> GetInstanceWithTransitionAsync(LifeCycleKey key) {
            if (key.Type != WorkFlowEntityKeyType.Parent) {
                if (key.keys[0] == null || !int.TryParse(key.keys[0].ToString(), out var definitionVersion)) throw new ArgumentException("Instance key A must be a valid integer value (definitionVersionId).");
                if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            }
            return await GetInstanceAsync(key);
        }

        public async Task<LifeCycleInstance?> GetInstanceAsync(LifeCycleKey instanceKey) {
            var fb = await Repository.Get(WorkFlowEntity.Instance, instanceKey);
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return null;
            return MapInstance(fb.Result);
        }

        public async Task<bool> InitializeAsync(LifeCycleKey instanceKey, WorkFlowInstanceFlag flags = WorkFlowInstanceFlag.Active) {
            try {
                var existing = await GetInstanceWithTransitionAsync(instanceKey);
                if (existing != null) return true;
                var agwInfo = Repository.AdapterGatewayInfo;
                var input = instanceKey.ParseInstanceKey(agwInfo.agw,agwInfo.adapterKey);
                var initFb = await Repository.GetStateByFlags(input.definitionVersion, WorkFlowStateFlag.IsInitial);
                EnsureSuccess(initFb, "State_GetByFlags(IsInitial)");
                var initRow = (initFb.Result != null && initFb.Result.Count > 0) ? initFb.Result[0] : null;
                if (initRow == null) throw new InvalidOperationException($"No initial state found for def_version={input.definitionVersion}.");

                var initialStateId = initRow.GetInt("id");

                //During initialization , last_event is always null
                var insFb = await Repository.UpsertInstance(input.definitionVersion, initialStateId, null, input.externalRef, flags);
                EnsureSuccess(insFb, "Instance_Upsert");
                return true;
            } catch (Exception ex) {
                NotifyError(new LifeCycleError() {
                    Exception = ex,
                    Reference = instanceKey,
                    Operation = "InitializeAsync"
                });
                return false;
            }
        }

        public async Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(LifeCycleKey instanceKey, int skip = 0, int limit = 200) {
            var instance = await GetInstanceWithTransitionAsync(instanceKey);
            if (instance == null) return Array.Empty<LifeCycleTransitionLog>();

            var rowsFb = await Repository.GetTransitionLogList(new LifeCycleKey(WorkFlowEntityKeyType.Id, instance.Id), skip, limit);
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

            var updFb = await Repository.UpdateInstanceState(new LifeCycleKey(WorkFlowEntityKeyType.Id, instance.Id), newStateId, 0, instance.Flags);
            EnsureSuccess(updFb, "Instance_UpdateState");
            return updFb.Result;
        }
    }
}
