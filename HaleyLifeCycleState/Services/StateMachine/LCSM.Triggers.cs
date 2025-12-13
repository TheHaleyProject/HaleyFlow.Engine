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

        public async Task<bool> TriggerAsync(LifeCycleKey instanceKey, int eventCode, string? actor = null, string? comment = null, object? context = null) {
            try {
                var instance = await GetInstanceWithTransitionAsync(instanceKey);
                if (instance == null) throw new InvalidOperationException("Instance not found.");
                var input = ParseInstanceKey(instanceKey);
                var evFb = await Repository.Get(LifeCycleEntity.Event, new LifeCycleKey(LifeCycleKeyType.Composite, input.definitionVersion, eventCode));
                EnsureSuccess(evFb, "Get(Event by code)");
                if (evFb.Result == null || evFb.Result.Count == 0) throw new InvalidOperationException($"Event code={eventCode} not found.");

                var evResult = MapEvent(evFb.Result);

                var trFb = await Repository.GetTransition(instance.CurrentState, evResult.Id, input.definitionVersion);
                EnsureSuccess(trFb, "Transition_Get");
                if (trFb.Result == null || trFb.Result.Count == 0) throw new InvalidOperationException($"No transition for state={instance.CurrentState}, eventId={evResult.Id}.");

                var trResult = MapTransition(trFb.Result);  

                var actorValue = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
                var metadata = BuildMetadata(comment, context);

                var logIdFb = await Repository.AppendTransitionLog(instance.Id, trResult.FromState, trResult.ToState, evResult.Id, actorValue, metadata);
                EnsureSuccess(logIdFb, "TransitionLog_Append");
                var logId = logIdFb.Result;

                var updFb = await Repository.UpdateInstanceState(new LifeCycleKey(LifeCycleKeyType.Id, instance.Id), trResult.ToState, evResult.Id, instance.Flags);
                EnsureSuccess(updFb, "Instance_UpdateState");

                //var msgId = Guid.NewGuid().ToString(); //Let the database create the unique message Id.
                var ackFb = await Repository.InsertAck(logId, consumer: 0, ackStatus: (int) LifeCycleAckStatus.Pending);
                EnsureSuccess(ackFb, "Ack_Insert");

                var occurred = new TransitionOccurred {
                    MessageId = ackFb.Result.GetString("message_id"),
                    TransitionLogId = logId,
                    InstanceId = instance.Id,
                    DefinitionVersion = instance.DefinitionVersion,
                    ExternalRef = instance.ExternalRef ?? string.Empty,
                    FromStateId = trResult.FromState,
                    ToStateId = trResult.ToState,
                    EventId = evResult.Id,
                    EventCode = eventCode,
                    EventName = evResult.DisplayName,
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

        public async Task<bool> TriggerAsync(LifeCycleKey instanceKey, string eventName, string? actor = null, string? comment = null, object? context = null) {
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentNullException(nameof(eventName));
            var input = ParseInstanceKey(instanceKey);
            try {
                var fb = await Repository.Get(LifeCycleEntity.Event, new LifeCycleKey(LifeCycleKeyType.Composite, input.definitionVersion, eventName.Trim()));
                EnsureSuccess(fb, "Get(Event by name)");
                if (fb.Result == null || fb.Result.Count == 0) throw new InvalidOperationException($"Event '{eventName}' not found.");

                var code = fb.Result.GetInt("code");
                if (code <= 0) throw new ArgumentException("Invalid event code retrieved. Code needs to be a valid positive number");
                return await TriggerAsync(instanceKey, code, actor, comment, context);
            } catch {
                if (ThrowExceptions || Repository.ThrowExceptions) throw;
                return false;
            }
        }
    }
}
