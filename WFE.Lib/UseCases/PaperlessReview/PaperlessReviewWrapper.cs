using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.PaperlessReview {
    [LifeCycleDefinition(DefinitionNameConst)]
    public sealed class PaperlessReviewWrapper : InteractiveHookWrapperBase {
        public const string DefinitionNameConst = "PaperlessReview";
        protected override string DefinitionName => DefinitionNameConst;

        public PaperlessReviewWrapper(IWorkFlowEngineAccessor engineAccessor, UseCaseRuntimeOptions options)
            : base(engineAccessor, options) { }

        public async Task<string?> TryStartRandomEntityAsync(string sourceUseCase, CancellationToken ct) {
            var timeout = Options.ConfirmationTimeout;
            var timeoutText = timeout > TimeSpan.Zero ? $", auto-yes in {timeout.TotalSeconds:0}s" : string.Empty;
            var createNow = await AskConfirmationAsync(
                $"[DRIVER] Create a new random paperless review entity now? (Y/N, Enter=Y{timeoutText})",
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
                Event = "3000",
                Actor = "wfe.test.wrapper-driver",
                AckRequired = true,
                Payload = new Dictionary<string, object> {
                    ["source"] = "WFE.Test",
                    ["useCase"] = sourceUseCase,
                    ["driver"] = nameof(PaperlessReviewWrapper)
                },
                Metadata = "debug;bulk-seed;paperless-review"
            }, ct);

            Console.WriteLine($"[DRIVER] entity={entityId} startEvent=3000 applied={trigger.Applied} instanceId={trigger.InstanceId} reason={trigger.Reason}");
            return trigger.Applied ? entityId : null;
        }

        [HookHandler("APP.REVIEW.COMPLIANCE")]
        private Task<AckOutcome> OnComplianceReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Compliance review approved?",
                PickEvent(evt.OnSuccessEvent, "3001"),
                PickEvent(evt.OnFailureEvent, "3002"));

        [HookHandler("APP.REVIEW.STRUCTURAL")]
        private Task<AckOutcome> OnStructuralReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Structural review approved?",
                PickEvent(evt.OnSuccessEvent, "3003"),
                PickEvent(evt.OnFailureEvent, "3004"));

        [HookHandler("APP.REVIEW.MEP")]
        private Task<AckOutcome> OnMepReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "MEP review approved?",
                PickEvent(evt.OnSuccessEvent, "3005"),
                PickEvent(evt.OnFailureEvent, "3006"));

        [HookHandler("APP.REVIEW.COMMENTS.CONSOLIDATE")]
        private Task<AckOutcome> OnCommentConsolidationAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Comments consolidated?",
                PickEvent(evt.OnSuccessEvent, "3007"),
                null);

        [HookHandler("APP.REVIEW.GRADING")]
        private Task<AckOutcome> OnGradingAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Final grading passed?",
                PickEvent(evt.OnSuccessEvent, "3008"),
                PickEvent(evt.OnFailureEvent, "3009"));

        [HookHandler("APP.REVIEW.REVISION.NOTIFY")]
        private Task<AckOutcome> OnRevisionNotifiedAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Consultant notified and ready to resubmit?",
                PickEvent(evt.OnSuccessEvent, "3010"),
                null);

        [HookHandler("APP.REVIEW.RESUBMISSION.RECEIVED")]
        private Task<AckOutcome> OnResubmissionReceivedAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Resubmission received and restart review?",
                PickEvent(evt.OnSuccessEvent, "3011"),
                null);

        [HookHandler("APP.REVIEW.REVISION.EXPIRED")]
        private Task<AckOutcome> OnRevisionExpiredAsync(ILifeCycleHookEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] revision expired route={evt.Route} entity={evt.EntityId} (no follow-up trigger).");
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
    }
}
