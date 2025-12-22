using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {
        public async Task<IFeedback<StateMachineNotice>> TriggerAsync(LifeCycleKey instanceKey, int eventCode, string? actor = null, string? comment = null, object? context = null) {
            try {
                var fbResult = new Feedback<StateMachineNotice>();
                var instance = await GetInstanceWithTransitionAsync(instanceKey);
                if (instance == null) throw new InvalidOperationException("Instance not found.");
                var agwInfo = Repository.AdapterGatewayInfo;
                var input = instanceKey.ParseInstanceKey(agwInfo.agw,agwInfo.adapterKey);
                var evFb = await Repository.Get(LifeCycleEntity.Event, new LifeCycleKey(LifeCycleKeyType.Composite, input.definitionVersion, eventCode));

                if (!evFb.Status || evFb.Result == null || evFb.Result.Count == 0) {
                    return fbResult.SetResult(new StateMachineNotice() {
                        Reference = instanceKey,
                        Data = instance,
                        CorrelationId = instance.Id,
                        Message = $"Failed to retrieve event for code='{eventCode}'. No such event exists.",
                        Operation = "TriggerAsync"
                    });
                }

                var evResult = MapEvent(evFb.Result);

                //If we are already in the same transition state and the application is trying to trigger the same event, we raise an error or better send a duplicate transition error.

                //todo: Check if we are already in the same state, then there is not point in triggering the same event again.

                var trFb = await Repository.GetTransition(instance.CurrentState, evResult.Id, input.definitionVersion);
                if (!trFb.Status || trFb.Result == null || trFb.Result.Count == 0) {
                    var latestTransitionFb = await Repository.GetLatestTransitionLog(instance.Id);
                    return fbResult.SetResult(new StateMachineNotice() {
                        Reference = instanceKey,
                        Data = latestTransitionFb.Result as object ?? "Latest Transition Log not found",
                        CorrelationId = instance.Id,
                        Message = $"Failed to retrieve transition for state='{instance.CurrentState}', eventId='{evResult.Id}'. No such transition exists. Last event for the instance is {instance.LastEvent}",
                        Operation = "TriggerAsync"
                    });
                }

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
                NotifyTransition(occurred);
                return fbResult.SetStatus(true).SetResult(new StateMachineNotice() {
                    Reference = instanceKey,
                    Data = new { eventCode, actor },
                    CorrelationId = instance.Id,
                    Operation = "TriggerAsync"
                });
            } catch (Exception ex) {
                NotifyError(new StateMachineError() {
                    Exception = ex,
                    Reference = instanceKey,
                    Data = new { eventCode, actor},
                    Operation = "TriggerAsync"
                });
                return await Task.FromException<IFeedback<StateMachineNotice>>(ex);
            }
        }

        public async Task<IFeedback<StateMachineNotice>> TriggerAsync(LifeCycleKey instanceKey, string eventName, string? actor = null, string? comment = null, object? context = null) {
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentNullException(nameof(eventName));
            var agwInfo = Repository.AdapterGatewayInfo;
            var input = instanceKey.ParseInstanceKey(agwInfo.agw, agwInfo.adapterKey);
            try {
                var fb = await Repository.Get(LifeCycleEntity.Event, new LifeCycleKey(LifeCycleKeyType.Composite, input.definitionVersion, eventName.Trim()));
                EnsureSuccess(fb, "Get(Event by name)");
                if (fb.Result == null || fb.Result.Count == 0) throw new InvalidOperationException($"Event '{eventName}' not found.");

                var code = fb.Result.GetInt("code");
                if (code <= 0) throw new ArgumentException("Invalid event code retrieved. Code needs to be a valid positive number");
                return await TriggerAsync(instanceKey, code, actor, comment, context);
            } catch (Exception ex) {
                NotifyError(new StateMachineError() {
                    Exception = ex,
                    Reference = instanceKey,
                    Data = new { eventName, actor },
                    Operation = "TriggerAsync"
                });
                return await Task.FromException<IFeedback<StateMachineNotice>>(ex);
            }
        }
    }
}
