using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.PaperlessReview {
    [LifeCycleDefinition(PaperlessReviewUseCaseSettings.DefinitionNameConst)]
    internal sealed class PaperlessReviewWrapper : LifeCycleWrapper {
        private static readonly ConcurrentDictionary<string, int> ReviewRounds = new(StringComparer.OrdinalIgnoreCase);

        private readonly IWorkFlowEngine _engine;
        private readonly PaperlessReviewUseCaseSettings _settings;

        public PaperlessReviewWrapper(IWorkFlowEngine engine, PaperlessReviewUseCaseSettings settings) {
            _engine = engine;
            _settings = settings;
        }

        [HookHandler("APP.REVIEW.COMPLIANCE")]
        private Task<AckOutcome> OnComplianceReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "3001"));

        [HookHandler("APP.REVIEW.STRUCTURAL")]
        private Task<AckOutcome> OnStructuralReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "3003"));

        [HookHandler("APP.REVIEW.MEP")]
        private Task<AckOutcome> OnMepReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "3005"));

        [HookHandler("APP.REVIEW.COMMENTS.CONSOLIDATE")]
        private Task<AckOutcome> OnCommentConsolidationAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "3007"));

        [HookHandler("APP.REVIEW.GRADING")]
        private Task<AckOutcome> OnGradingAsync(ILifeCycleHookEvent evt, ConsumerContext ctx) {
            var key = string.IsNullOrWhiteSpace(evt.EntityId) ? "(empty-entity)" : evt.EntityId;
            var round = ReviewRounds.AddOrUpdate(key, 1, static (_, current) => current + 1);
            var nextEvent = round == 1
                ? PickEvent(evt.OnFailureEvent, "3009")
                : PickEvent(evt.OnSuccessEvent, "3008");

            Console.WriteLine($"[CONSUMER] grading round={round} entity={key} -> nextEvent={nextEvent}");
            return TriggerNextAsync(evt, ctx, nextEvent);
        }

        [HookHandler("APP.REVIEW.REVISION.NOTIFY")]
        private Task<AckOutcome> OnRevisionNotifiedAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "3010"));

        [HookHandler("APP.REVIEW.RESUBMISSION.RECEIVED")]
        private Task<AckOutcome> OnResubmissionReceivedAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "3011"));

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
