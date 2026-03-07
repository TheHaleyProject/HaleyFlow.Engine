using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.ChangeRequest {
    [LifeCycleDefinition(ChangeRequestUseCaseSettings.DefinitionNameConst)]
    internal sealed class ChangeRequestWrapper : LifeCycleWrapper {
        private static readonly ConcurrentDictionary<string, int> CostReviewRounds = new(StringComparer.OrdinalIgnoreCase);

        private readonly IWorkFlowEngine _engine;
        private readonly ChangeRequestUseCaseSettings _settings;

        public ChangeRequestWrapper(IWorkFlowEngine engine, ChangeRequestUseCaseSettings settings) {
            _engine = engine;
            _settings = settings;
        }

        [HookHandler("APP.CHANGE.IMPACT.ASSESS")]
        private Task<AckOutcome> OnImpactAssessmentAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "4001"));

        [HookHandler("APP.CHANGE.COST.REVIEW")]
        private Task<AckOutcome> OnCostReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx) {
            var key = string.IsNullOrWhiteSpace(evt.EntityId) ? "(empty-entity)" : evt.EntityId;
            var round = CostReviewRounds.AddOrUpdate(key, 1, static (_, current) => current + 1);
            var nextEvent = round == 1
                ? PickEvent(evt.OnFailureEvent, "4004")
                : PickEvent(evt.OnSuccessEvent, "4003");

            Console.WriteLine($"[CONSUMER] cost review round={round} entity={key} -> nextEvent={nextEvent}");
            return TriggerNextAsync(evt, ctx, nextEvent);
        }

        [HookHandler("APP.CHANGE.SCHEDULE.REVIEW")]
        private Task<AckOutcome> OnScheduleReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "4005"));

        [HookHandler("APP.CHANGE.STEERING.DECIDE")]
        private Task<AckOutcome> OnSteeringDecisionAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "4007"));

        [HookHandler("APP.CHANGE.REWORK.REQUEST")]
        private Task<AckOutcome> OnReworkRequestedAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "4009"));

        protected override Task<AckOutcome> OnUnhandledTransitionAsync(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] transition event={evt.EventCode} state={evt.FromStateId}->{evt.ToStateId} (no custom action)");
            return Task.FromResult(AckOutcome.Processed);
        }

        protected override Task<AckOutcome> OnUnhandledHookAsync(ILifeCycleHookEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] hook route={evt.Route} (no custom action)");
            return Task.FromResult(AckOutcome.Processed);
        }

        private static string PickEvent(string? preferred, string fallback)
            => !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;

        private async Task<AckOutcome> TriggerNextAsync(ILifeCycleHookEvent evt, ConsumerContext ctx, string nextEventCode) {
            if (!int.TryParse(nextEventCode, out _)) {
                Console.WriteLine($"[CONSUMER] route={evt.Route} has non-numeric next event '{nextEventCode}', skipping trigger.");
                return AckOutcome.Processed;
            }

            var request = new LifeCycleTriggerRequest {
                EnvCode = _settings.EnvCode,
                DefName = _settings.DefName,
                EntityId = evt.EntityId,
                Event = nextEventCode,
                Actor = "wfe.test.consumer",
                AckRequired = true,
                Payload = new Dictionary<string, object> {
                    ["fromRoute"] = evt.Route,
                    ["consumerWfId"] = ctx.WfId,
                    ["consumerId"] = ctx.ConsumerId
                }
            };

            var result = await _engine.TriggerAsync(request, ctx.CancellationToken);
            Console.WriteLine($"[CONSUMER] route={evt.Route} -> event={nextEventCode} applied={result.Applied} reason={result.Reason}");
            return AckOutcome.Processed;
        }
    }
}
