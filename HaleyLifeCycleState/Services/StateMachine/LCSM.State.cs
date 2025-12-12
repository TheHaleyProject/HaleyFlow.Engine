using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        #region Validation

        public async Task<bool> ValidateTransitionAsync(int definitionVersion, int fromStateId, int toStateId) {
            var fb = await _repo.GetOutgoingTransitions(fromStateId, definitionVersion);
            if (fb == null || !fb.Status || fb.Result == null) return false;
            return fb.Result.Any(r => ToInt(r["to_state"]) == toStateId);
        }

        #endregion

        #region Current State

        public async Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, string externalRef) {
            var instance = await GetInstanceAsync(definitionVersion, externalRef)
                ?? throw new InvalidOperationException($"Instance not found for def_version={definitionVersion}, external_ref='{externalRef}'");

            var fb = await _repo.GetStatesByVersion(definitionVersion);
            await ThrowIfFailed(fb, "GetStatesByVersion");
            if (fb.Result == null) throw new InvalidOperationException($"No states for def_version {definitionVersion}");

            var row = fb.Result.FirstOrDefault(r => ToInt(r["id"]) == instance.CurrentState);
            return row != null ? MapState(row) : throw new InvalidOperationException($"State not found for id {instance.CurrentState}");
        }

        public Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, Guid externalRefId) =>
            GetCurrentStateAsync(definitionVersion, externalRefId.ToString());

        #endregion

        #region Transition History

        public async Task<IReadOnlyList<LifeCycleTransitionLog?>> GetTransitionHistoryAsync(int definitionVersion, string externalRef) {
            var instance = await GetInstanceAsync(definitionVersion, externalRef);
            if (instance == null) return Array.Empty<LifeCycleTransitionLog>();

            var fb = await _repo.GetLogsByInstance(instance.Id);
            if (fb == null || !fb.Status || fb.Result == null) return Array.Empty<LifeCycleTransitionLog>();

            var list = new List<LifeCycleTransitionLog>();
            foreach (var r in fb.Result) {
                list.Add(new LifeCycleTransitionLog {
                    Id = ToLong(r["id"]),
                    InstanceId = ToLong(r["instance_id"]),
                    FromState = ToInt(r["from_state"]),
                    ToState = ToInt(r["to_state"]),
                    Event = ToInt(r["event"]),
                    Flags = (LifeCycleTransitionLogFlag)ToInt(r["flags"]),
                    Created = Convert.ToDateTime(r["created"])
                });
            }

            return list;
        }

        public Task<IReadOnlyList<LifeCycleTransitionLog?>> GetTransitionHistoryAsync(int definitionVersion, Guid externalRefId) =>
            GetTransitionHistoryAsync(definitionVersion, externalRefId.ToString());

        #endregion

        #region Force Update

        public async Task ForceUpdateStateAsync(
            int definitionVersion,
            string externalRef,
            int newStateId,
            LifeCycleTransitionLogFlag flags = LifeCycleTransitionLogFlag.System,
            string? actor = null,
            string? metadata = null) {

            var instance = await GetInstanceAsync(definitionVersion, externalRef)
                ?? throw new InvalidOperationException($"Instance not found for def_version={definitionVersion}, external_ref='{externalRef}'");

            var actorVal = string.IsNullOrWhiteSpace(actor) ? "system" : actor;
            var metaVal = string.IsNullOrWhiteSpace(metadata) ? "Force update" : metadata;

            var logFb = await _repo.LogTransition(instance.Id, instance.CurrentState, newStateId, 0, actorVal, flags, metaVal);
            await ThrowIfFailed(logFb, "LogTransition");

            var updFb = await _repo.UpdateInstanceState(instance.Id, newStateId, 0, instance.Flags);
            await ThrowIfFailed(updFb, "UpdateInstanceState");
        }

        public Task ForceUpdateStateAsync(
            int definitionVersion,
            Guid externalRefId,
            int newStateId,
            LifeCycleTransitionLogFlag flags = LifeCycleTransitionLogFlag.System,
            string? actor = null,
            string? metadata = null) =>
            ForceUpdateStateAsync(definitionVersion, externalRefId.ToString(), newStateId, flags, actor, metadata);

        #endregion

        #region State Checks

        public async Task<bool> IsFinalStateAsync(int definitionVersion, int stateId) {
            var fb = await _repo.GetStatesByVersion(definitionVersion);
            if (fb == null || !fb.Status || fb.Result == null) return false;

            var row = fb.Result.FirstOrDefault(r => ToInt(r["id"]) == stateId);
            if (row == null) return false;

            var flags = (LifeCycleStateFlag)ToInt(row["flags"]);
            return flags.HasFlag(LifeCycleStateFlag.IsFinal);
        }

        public async Task<bool> IsInitialStateAsync(int definitionVersion, int stateId) {
            var fb = await _repo.GetStatesByVersion(definitionVersion);
            if (fb == null || !fb.Status || fb.Result == null) return false;

            var row = fb.Result.FirstOrDefault(r => ToInt(r["id"]) == stateId);
            if (row == null) return false;

            var flags = (LifeCycleStateFlag)ToInt(row["flags"]);
            return flags.HasFlag(LifeCycleStateFlag.IsInitial);
        }

        #endregion
    }
}
