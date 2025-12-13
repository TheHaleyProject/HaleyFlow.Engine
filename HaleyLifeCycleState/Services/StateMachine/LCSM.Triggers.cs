using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        private async Task RaiseTransitionAsync(TransitionOccurred occurred) {
            var handler = TransitionRaised;
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList()) {
                try {
                    if (d is Func<TransitionOccurred, Task> asyncHandler) await asyncHandler(occurred);
                    else d.DynamicInvoke(occurred);
                } catch { }
            }
        }

        public async Task<bool> CanTransitionAsync(int definitionVersion, int fromStateId, int eventCode) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (fromStateId <= 0) throw new ArgumentOutOfRangeException(nameof(fromStateId));

            var evFb = await Repository.Get(LifeCycleEntity.Event, new LifeCycleKey(LifeCycleKeyType.Composite, definitionVersion, eventCode));
            if (evFb == null || !evFb.Status || evFb.Result == null || evFb.Result.Count == 0) return false;

            var eventId = evFb.Result.GetInt("id");
            var trFb = await Repository.GetTransition(fromStateId, eventId, definitionVersion);
            return trFb != null && trFb.Status && trFb.Result != null && trFb.Result.Count > 0;
        }

        public async Task<bool> TriggerAsync(int definitionVersion, LifeCycleKey instanceKey, int eventCode, string? actor = null, string? comment = null, object? context = null) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));

            try {
                var instance = await GetInstanceAsync(definitionVersion, instanceKey);
                if (instance == null) throw new InvalidOperationException("Instance not found.");

                var evFb = await Repository.Get(LifeCycleEntity.Event, new LifeCycleKey(LifeCycleKeyType.Composite, definitionVersion, eventCode));
                EnsureSuccess(evFb, "Get(Event by code)");
                if (evFb.Result == null || evFb.Result.Count == 0) throw new InvalidOperationException($"Event code={eventCode} not found.");

                var eventId = evFb.Result.GetInt("id");
                var eventName = evFb.Result.GetString("display_name") ?? evFb.Result.GetString("name") ?? eventCode.ToString();

                var trFb = await Repository.GetTransition(instance.CurrentState, eventId, definitionVersion);
                EnsureSuccess(trFb, "Transition_Get");
                if (trFb.Result == null || trFb.Result.Count == 0) throw new InvalidOperationException($"No transition for state={instance.CurrentState}, eventId={eventId}.");

                var fromStateId = trFb.Result.GetInt("from_state");
                var toStateId = trFb.Result.GetInt("to_state");

                var actorValue = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
                var metadata = BuildMetadata(comment, context);

                var logIdFb = await Repository.AppendTransitionLog(instance.Id, fromStateId, toStateId, eventId, actorValue, metadata);
                EnsureSuccess(logIdFb, "TransitionLog_Append");
                var logId = logIdFb.Result;

                var updFb = await Repository.UpdateInstanceState(new LifeCycleKey(LifeCycleKeyType.Id, instance.Id), toStateId, eventId, instance.Flags);
                EnsureSuccess(updFb, "Instance_UpdateState");

                var msgId = Guid.NewGuid().ToString("N");
                var ackFb = await Repository.InsertAck(logId, consumer: 1, ackStatus: 1, messageId: msgId);
                EnsureSuccess(ackFb, "Ack_Insert");

                var occurred = new TransitionOccurred {
                    TransitionLogId = logId,
                    InstanceId = instance.Id,
                    DefinitionVersion = instance.DefinitionVersion,
                    ExternalRef = instance.ExternalRef ?? string.Empty,
                    FromStateId = fromStateId,
                    ToStateId = toStateId,
                    EventId = eventId,
                    EventCode = eventCode,
                    EventName = eventName,
                    Actor = actorValue,
                    Metadata = metadata,
                    Created = DateTime.UtcNow
                };

                await RaiseTransitionAsync(occurred);
                return true;
            } catch {
                if (ThrowExceptions || Repository.ThrowExceptions) throw;
                return false;
            }
        }

        public async Task<bool> TriggerByNameAsync(int definitionVersion, LifeCycleKey instanceKey, string eventName, string? actor = null, string? comment = null, object? context = null) {
            if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentNullException(nameof(eventName));

            try {
                var fb = await Repository.Get(LifeCycleEntity.Event, new LifeCycleKey(LifeCycleKeyType.Composite, definitionVersion, eventName.Trim()));
                EnsureSuccess(fb, "Get(Event by name)");
                if (fb.Result == null || fb.Result.Count == 0) throw new InvalidOperationException($"Event '{eventName}' not found.");

                var code = fb.Result.GetInt("code");
                if (code <= 0) code = fb.Result.GetInt("id");
                return await TriggerAsync(definitionVersion, instanceKey, code, actor, comment, context);
            } catch {
                if (ThrowExceptions || Repository.ThrowExceptions) throw;
                return false;
            }
        }
    }
}
