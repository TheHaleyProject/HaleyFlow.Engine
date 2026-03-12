using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.ChangeRequest {
    [LifeCycleDefinition(DefinitionNameConst)]
    public sealed class ChangeRequestWrapper : InteractiveHookWrapperBase {
        public const string DefinitionNameConst = "ProjectChangeRequest";
        protected override string DefinitionName => DefinitionNameConst;

        public ChangeRequestWrapper(IWorkFlowEngineAccessor engineAccessor, UseCaseRuntimeOptions options)
            : base(engineAccessor, options) { }

        public async Task<string?> TryStartRandomEntityAsync(string sourceUseCase, CancellationToken ct) {
            var timeout = Options.ConfirmationTimeout;
            var timeoutText = timeout > TimeSpan.Zero ? $", auto-yes in {timeout.TotalSeconds:0}s" : string.Empty;
            var createNow = await AskConfirmationAsync(
                $"[DRIVER] Create a new random change request entity now? (Y/N, Enter=Y{timeoutText})",
                ConsoleKey.Y,
                timeout,
                ct);

            if (!createNow) {
                Console.WriteLine("[DRIVER] Entity creation skipped by user choice.");
                return null;
            }

            var entityId = Guid.NewGuid().ToString("N");
            var engine = await EngineAccessor.GetEngineAsync(ct);
            var trigger = await engine.TriggerAsync(new LifeCycleTriggerRequest {
                EnvCode = Options.EnvCode,
                DefName = DefinitionNameConst,
                EntityId = entityId,
                Event = "4000",
                Actor = "wfe.test.wrapper-driver",
                AckRequired = true,
                Payload = new Dictionary<string, object> {
                    ["source"] = "WFE.Test",
                    ["useCase"] = sourceUseCase,
                    ["driver"] = nameof(ChangeRequestWrapper)
                },
                Metadata = "debug;bulk-seed;change-request"
            }, ct);

            Console.WriteLine($"[DRIVER] entity={entityId} startEvent=4000 applied={trigger.Applied} instanceId={trigger.InstanceId} reason={trigger.Reason}");
            return trigger.Applied ? entityId : null;
        }

        [HookHandler("APP.CHANGE.IMPACT.ASSESS")]
        private Task<AckOutcome> OnImpactAssessmentAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Impact assessment approved?",
                PickEvent(evt.OnSuccessEvent, "4001"),
                PickEvent(evt.OnFailureEvent, "4002"),
                ResolveExecutionModeFromParams(evt));

        [HookHandler("APP.CHANGE.COST.REVIEW")]
        private Task<AckOutcome> OnCostReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Cost review approved?",
                PickEvent(evt.OnSuccessEvent, "4003"),
                PickEvent(evt.OnFailureEvent, "4004"),
                BusinessActionExecutionMode.ForceRun);

        [HookHandler("APP.CHANGE.SCHEDULE.REVIEW")]
        private Task<AckOutcome> OnScheduleReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Schedule review approved?",
                PickEvent(evt.OnSuccessEvent, "4005"),
                PickEvent(evt.OnFailureEvent, "4006"),
                BusinessActionExecutionMode.ForceRun);

        [HookHandler("APP.CHANGE.STEERING.DECIDE")]
        private Task<AckOutcome> OnSteeringDecisionAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Steering decision approved?",
                PickEvent(evt.OnSuccessEvent, "4007"),
                PickEvent(evt.OnFailureEvent, "4008"),
                BusinessActionExecutionMode.ForceRun);

        [HookHandler("APP.CHANGE.REWORK.REQUEST")]
        private Task<AckOutcome> OnReworkRequestedAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Submit rework now?",
                PickEvent(evt.OnSuccessEvent, "4009"),
                null,
                BusinessActionExecutionMode.SkipIfCompleted);

        [TransitionHandler(4000)]
        private Task<AckOutcome> OnAutoStartTransitionAsync(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] transition-handler event={evt.EventCode} entity={evt.EntityId} state={evt.FromStateId}->{evt.ToStateId}");
            return Task.FromResult(AckOutcome.Processed);
        }

        protected override Task<AckOutcome> OnUnhandledTransitionAsync(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] transition event={evt.EventCode} state={evt.FromStateId}->{evt.ToStateId} (no custom action)");
            return Task.FromResult(AckOutcome.Processed);
        }

        protected override Task<AckOutcome> OnUnhandledHookAsync(ILifeCycleHookEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] hook route={evt.Route} (no custom action)");
            return Task.FromResult(AckOutcome.Processed);
        }

        protected override Task BeforeDecisionAsync(ILifeCycleHookEvent evt, string decisionMessage, CancellationToken ct)
            => UpsertRuntimeStatusAsync(
                evt,
                "Running",
                new { route = evt.Route, entity = evt.EntityId, question = decisionMessage },
                ct);

        protected override Task AfterDecisionAsync(ILifeCycleHookEvent evt, bool yes, bool hasFailurePath, CancellationToken ct)
            => UpsertRuntimeStatusAsync(
                evt,
                yes ? "Approved" : (hasFailurePath ? "Rejected" : "Retry"),
                new { route = evt.Route, entity = evt.EntityId, decision = yes ? "yes" : (hasFailurePath ? "no" : "retry") },
                ct);

        private static BusinessActionExecutionMode ResolveExecutionModeFromParams(ILifeCycleHookEvent evt) {
            // Example override:
            // params: { "forceBusinessAction": true } => ForceRun
            if (evt.Params == null || evt.Params.Count < 1) return BusinessActionExecutionMode.SkipIfCompleted;
            for (var i = 0; i < evt.Params.Count; i++) {
                var data = evt.Params[i]?.Data;
                if (data == null || data.Count < 1) continue;
                if (TryReadBool(data, "forceBusinessAction", out var force) && force) {
                    return BusinessActionExecutionMode.ForceRun;
                }
            }
            return BusinessActionExecutionMode.SkipIfCompleted;
        }

        private static bool TryReadBool(IReadOnlyDictionary<string, object?> data, string key, out bool value) {
            value = false;
            if (!data.TryGetValue(key, out var raw) || raw == null) return false;

            switch (raw) {
                case bool b:
                    value = b;
                    return true;
                case string s when bool.TryParse(s, out var parsed):
                    value = parsed;
                    return true;
                case int i:
                    value = i != 0;
                    return true;
                case long l:
                    value = l != 0;
                    return true;
                case JsonElement je when je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.False:
                    value = je.GetBoolean();
                    return true;
                case JsonElement je when je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var p2):
                    value = p2;
                    return true;
                case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var p3):
                    value = p3 != 0;
                    return true;
                default:
                    return false;
            }
        }

        private async Task<long> UpsertRuntimeStatusAsync(
            ILifeCycleHookEvent evt,
            string status,
            object? data,
            CancellationToken ct) {
            var engine = await EngineAccessor.GetEngineAsync(ct);
            return await engine.UpsertRuntimeAsync(new RuntimeLogByNameRequest {
                Instance = new LifeCycleInstanceKey { InstanceGuid = evt.InstanceGuid },
                Activity = evt.Route,
                Status = status,
                ActorId = "wfe.test.consumer",
                AckGuid = evt.AckGuid,
                Data = data ?? new { }
            }, ct);
        }
    }
}
