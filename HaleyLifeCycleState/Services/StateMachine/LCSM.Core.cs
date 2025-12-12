using Haley.Enums;
using Haley.Models;
using System;
using Haley.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        // Treat this as a "DELIVERED" acknowledgement (blue-tick).
        public async Task<bool> ReceiveAckAsync(string messageId) {
            var fb = await _repo.Ack_MarkDeliveredByMessage(messageId);
            return fb != null && fb.Status;
        }

        // Mark message as processed (double blue-tick / completed).
        public async Task<bool> ReceiveProcessedAckAsync(string messageId) {
            var fb = await _repo.Ack_MarkProcessedByMessage(messageId);
            return fb != null && fb.Status;
        }

        // Mark message as failed.
        public async Task<bool> ReceiveFailedAckAsync(string messageId) {
            var fb = await _repo.Ack_MarkFailedByMessage(messageId);
            return fb != null && fb.Status;
        }

        public async Task RetryUnackedAsync(int maxRetry = 10, int retryAfterMinutes = 2) {
            var fb = await _repo.Ack_GetDueForRetry(maxRetry, retryAfterMinutes);
            if (fb?.Result == null || fb.Result.Count == 0) return;

            foreach (var row in fb.Result) {
                var ackId = Convert.ToInt64(row["id"]);
                var transitionLogId = Convert.ToInt64(row["transition_log"]);
                var messageId = row["message_id"]?.ToString();

                // Re-publish the same notification (idempotent)
                // await _notifier.PublishTransition(messageId, transitionLogId);

                await _repo.Ack_BumpRetry(ackId);
            }
        }

        public void RegisterGuard(string transitionKey, Func<object?, Task<bool>> guardFunc) {
            _guards[transitionKey] = guardFunc;
        }

        #region Instance Retrieval

        public async Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, string externalRef) {
            externalRef = NormalizeExternalRef(externalRef);
            if (string.IsNullOrWhiteSpace(externalRef)) throw new ArgumentNullException(nameof(externalRef));

            var fb = await _repo.GetInstancesByRef(externalRef);
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return null;

            // external_ref can exist across versions; match by def_version.
            var match = fb.Result.FirstOrDefault(r => ToInt(r["def_version"]) == definitionVersion);
            return match != null ? MapInstance(match) : null;
        }

        public Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, Guid externalRefId) =>
            GetInstanceAsync(definitionVersion, externalRefId.ToString());

        #endregion

        #region Initialization

        public async Task InitializeAsync(int definitionVersion, string externalRef, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active) {
            externalRef = NormalizeExternalRef(externalRef);
            if (string.IsNullOrWhiteSpace(externalRef)) throw new ArgumentNullException(nameof(externalRef));

            var initFb = await _repo.GetInitialState(definitionVersion);
            await ThrowIfFailed(initFb, "GetInitialState");
            if (initFb.Result == null) throw new InvalidOperationException($"No initial state for def_version {definitionVersion}");

            int initStateId = ToInt(initFb.Result["id"]);

            // INSERT IGNORE + unique(def_version, external_ref) => safe for re-initialization.
            var regFb = await _repo.RegisterInstance(definitionVersion, initStateId, 0, externalRef, flags);
            await ThrowIfFailed(regFb, "RegisterInstance");
        }

        public Task InitializeAsync(int definitionVersion, Guid externalRefId, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active) =>
            InitializeAsync(definitionVersion, externalRefId.ToString(), flags);

        #endregion

        #region Trigger

        public async Task<bool> TriggerAsync(
            int definitionVersion,
            string externalRef,
            int toStateId,
            string? comment = null,
            string? actor = null,
            object? context = null,
            LifeCycleTransitionLogFlag flags = LifeCycleTransitionLogFlag.Manual) {

            LifeCycleTransitionLog? log = null;
            var _consumerId = 0; //To be changed later.
            try {
                var instance = await GetInstanceAsync(definitionVersion, externalRef);
                if (instance == null) throw new InvalidOperationException("Instance not found.");

                // enforce consistency (avoid someone passing wrong def version)
                if (instance.DefinitionVersion != definitionVersion)
                    throw new InvalidOperationException($"Instance def_version mismatch. Expected {definitionVersion}, got {instance.DefinitionVersion}.");

                var fromState = instance.CurrentState;

                var transitionFb = await _repo.GetOutgoingTransitions(fromState, definitionVersion);
                await ThrowIfFailed(transitionFb, "GetOutgoingTransitions");

                var transition = transitionFb.Result?.FirstOrDefault(x => Convert.ToInt32(x["to_state"]) == toStateId);
                if (transition == null) throw new InvalidOperationException($"Invalid transition {fromState} → {toStateId}");

                var guardKey = GetStr(transition, "guard_key");
                var eventId = Convert.ToInt32(transition["event"]);
                var transitionKey = !string.IsNullOrWhiteSpace(guardKey) ? guardKey : eventId.ToString();

                // Guard lookup
                if (_guards.TryGetValue(transitionKey, out var guardFunc)) {
                    bool allowed = await guardFunc(context);
                    if (!allowed) throw new InvalidOperationException($"Guard condition failed for transition {transitionKey}");
                }

                // Runtime log model mirrors transition_log table (no actor/metadata here)
                log = new LifeCycleTransitionLog {
                    InstanceId = instance.Id,
                    FromState = fromState,
                    ToState = toStateId,
                    Event = eventId,
                    Flags = flags,
                    Created = DateTime.UtcNow
                };

                await RaiseAsync(OnBeforeTransition, log);

                // Persist transition + update instance
                // Actor + metadata are stored in transition_data
                var meta = BuildMetadata(comment, context);
                var actorVal = string.IsNullOrWhiteSpace(actor) ? "system" : actor;

                var logIdFb = await _repo.LogTransition(instance.Id, fromState, toStateId, eventId, actorVal, flags, meta);
                await ThrowIfFailed(logIdFb, "LogTransition");

                var updFb = await _repo.UpdateInstanceState(instance.Id, toStateId, eventId, instance.Flags);
                await ThrowIfFailed(updFb, "UpdateInstanceState");

                // ACK: insert "SENT" record (ack_status = 1) tied to consumer + message id
                var messageId = Guid.NewGuid().ToString();
                await _repo.Ack_InsertWithMessage(logIdFb.Result, _consumerId, messageId, 1);

                await RaiseAsync(OnAfterTransition, log);
                return true;

            } catch (Exception ex) {
                await RaiseAsync(OnTransitionFailed, log, ex);
                return false;
            }
        }

        public Task<bool> TriggerAsync(
            int definitionVersion,
            Guid externalRefId,
            int toStateId,
            string? comment = null,
            string? actor = null,
            object? context = null,
            LifeCycleTransitionLogFlag flags = LifeCycleTransitionLogFlag.Manual) =>
            TriggerAsync(definitionVersion, externalRefId.ToString(), toStateId, comment, actor, context, flags);

        #endregion
    }
}
