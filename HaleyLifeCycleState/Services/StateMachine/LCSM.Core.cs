using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {
        public async Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, string externalRef) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            var normalizedRef = NormalizeExternalRef(externalRef);
            if (string.IsNullOrWhiteSpace(normalizedRef)) throw new ArgumentNullException(nameof(externalRef));

            var fb = await Repository.GetInstancesByRef(normalizedRef).ConfigureAwait(false);
            if (fb == null || !fb.Status || fb.Result == null) return null;

            var rows = fb.Result;
            var row = rows.FirstOrDefault(r => GetInt(r, "def_version") == definitionVersion);
            if (row == null) return null;

            return MapInstance(row);
        }

        public Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, Guid externalRefId) {
            return GetInstanceAsync(definitionVersion, externalRefId.ToString("D"));
        }

        public async Task InitializeAsync(int definitionVersion, string externalRef, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            var normalizedRef = NormalizeExternalRef(externalRef);
            if (string.IsNullOrWhiteSpace(normalizedRef)) throw new ArgumentNullException(nameof(externalRef));

            var existing = await GetInstanceAsync(definitionVersion, normalizedRef).ConfigureAwait(false);
            if (existing != null) return;

            var initFb = await Repository.GetInitialState(definitionVersion).ConfigureAwait(false);
            EnsureSuccess(initFb, "GetInitialState");
            if (initFb.Result == null) throw new InvalidOperationException($"No initial state found for definitionVersion={definitionVersion}.");

            var initialRow = initFb.Result;
            var initialStateId = GetInt(initialRow, "id");

            var regFb = await Repository.RegisterInstance(definitionVersion, initialStateId, 0, normalizedRef, flags).ConfigureAwait(false);
            EnsureSuccess(regFb, "RegisterInstance");
        }

        public Task InitializeAsync(int definitionVersion, Guid externalRefId, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active) {
            return InitializeAsync(definitionVersion, externalRefId.ToString("D"), flags);
        }

        public async Task<bool> ValidateTransitionAsync(int definitionVersion, int fromStateId, int eventCode) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (fromStateId <= 0) throw new ArgumentOutOfRangeException(nameof(fromStateId));

            var eventsFb = await Repository.GetEventsByVersion(definitionVersion).ConfigureAwait(false);
            EnsureSuccess(eventsFb, "GetEventsByVersion");
            var events = eventsFb.Result ?? new List<Dictionary<string, object>>();

            var evtRow = events.FirstOrDefault(r => GetInt(r, "code") == eventCode);
            if (evtRow == null) return false;

            var eventId = GetInt(evtRow, "id");

            var transFb = await Repository.GetOutgoingTransitions(fromStateId, definitionVersion).ConfigureAwait(false);
            EnsureSuccess(transFb, "GetOutgoingTransitions");
            var transitions = transFb.Result ?? new List<Dictionary<string, object>>();

            return transitions.Any(t => GetInt(t, "event") == eventId);
        }
    }
}
