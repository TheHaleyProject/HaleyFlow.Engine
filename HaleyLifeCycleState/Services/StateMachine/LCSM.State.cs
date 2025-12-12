using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        public async Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, string externalRef) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            var instance = await GetInstanceAsync(definitionVersion, externalRef).ConfigureAwait(false);
            if (instance == null) throw new InvalidOperationException("Instance not found.");

            var statesFb = await Repository.GetStatesByVersion(definitionVersion).ConfigureAwait(false);
            EnsureSuccess(statesFb, "GetStatesByVersion");
            var states = statesFb.Result ?? new List<Dictionary<string, object>>();

            var row = states.FirstOrDefault(r => GetInt(r, "id") == instance.CurrentState);
            if (row == null) throw new InvalidOperationException($"State id={instance.CurrentState} not found for def_version={definitionVersion}.");

            return MapState(row);
        }

        public Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, Guid externalRefId) {
            return GetCurrentStateAsync(definitionVersion, externalRefId.ToString("D"));
        }

        public async Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(int definitionVersion, string externalRef) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));

            var instance = await GetInstanceAsync(definitionVersion, externalRef).ConfigureAwait(false);
            if (instance == null) return Array.Empty<LifeCycleTransitionLog>();

            var logsFb = await Repository.GetLogsByInstance(instance.Id).ConfigureAwait(false);
            EnsureSuccess(logsFb, "GetLogsByInstance");
            var rows = logsFb.Result ?? new List<Dictionary<string, object>>();

            var list = new List<LifeCycleTransitionLog>();
            foreach (var r in rows) {
                var log = new LifeCycleTransitionLog {
                    Id = GetLong(r, "id"),
                    InstanceId = GetLong(r, "instance_id"),
                    FromState = GetInt(r, "from_state"),
                    ToState = GetInt(r, "to_state"),
                    Event = GetInt(r, "event"),
                    Created = DateTime.UtcNow
                };
                list.Add(log);
            }

            return list;
        }

        public Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(int definitionVersion, Guid externalRefId) {
            return GetTransitionHistoryAsync(definitionVersion, externalRefId.ToString("D"));
        }

        public async Task ForceUpdateStateAsync(int definitionVersion, string externalRef, int newStateId, string? actor = null, string? metadata = null) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (newStateId <= 0) throw new ArgumentOutOfRangeException(nameof(newStateId));

            var instance = await GetInstanceAsync(definitionVersion, externalRef).ConfigureAwait(false);
            if (instance == null) throw new InvalidOperationException("Instance not found.");

            var actorValue = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
            var meta = metadata ?? string.Empty;

            var logFb = await Repository.LogTransition(instance.Id, instance.CurrentState, newStateId, 0, actorValue, meta).ConfigureAwait(false);
            EnsureSuccess(logFb, "LogTransition");

            var updateFb = await Repository.UpdateInstanceState(instance.Id, newStateId, 0, instance.Flags).ConfigureAwait(false);
            EnsureSuccess(updateFb, "UpdateInstanceState");
        }

        public Task ForceUpdateStateAsync(int definitionVersion, Guid externalRefId, int newStateId, string? actor = null, string? metadata = null) {
            return ForceUpdateStateAsync(definitionVersion, externalRefId.ToString("D"), newStateId, actor, metadata);
        }

        public async Task<bool> IsFinalStateAsync(int definitionVersion, int stateId) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (stateId <= 0) throw new ArgumentOutOfRangeException(nameof(stateId));

            var statesFb = await Repository.GetStatesByVersion(definitionVersion).ConfigureAwait(false);
            EnsureSuccess(statesFb, "GetStatesByVersion");
            var states = statesFb.Result ?? new List<Dictionary<string, object>>();

            var row = states.FirstOrDefault(r => GetInt(r, "id") == stateId);
            if (row == null) return false;

            var flags = (LifeCycleStateFlag)GetInt(row, "flags");
            return flags.HasFlag(LifeCycleStateFlag.IsFinal);
        }

        public async Task<bool> IsInitialStateAsync(int definitionVersion, int stateId) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (stateId <= 0) throw new ArgumentOutOfRangeException(nameof(stateId));

            var statesFb = await Repository.GetStatesByVersion(definitionVersion).ConfigureAwait(false);
            EnsureSuccess(statesFb, "GetStatesByVersion");
            var states = statesFb.Result ?? new List<Dictionary<string, object>>();

            var row = states.FirstOrDefault(r => GetInt(r, "id") == stateId);
            if (row == null) return false;

            var flags = (LifeCycleStateFlag)GetInt(row, "flags");
            return flags.HasFlag(LifeCycleStateFlag.IsInitial);
        }
    }
}
