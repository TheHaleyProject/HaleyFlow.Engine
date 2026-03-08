using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.PaperlessReview {
    [LifeCycleDefinition(PaperlessReviewUseCaseSettings.DefinitionNameConst)]
    internal sealed class PaperlessReviewWrapper : LifeCycleWrapper {
        private static readonly SemaphoreSlim PromptLock = new(1, 1);

        private readonly IWorkFlowEngine _engine;
        private readonly PaperlessReviewUseCaseSettings _settings;

        public PaperlessReviewWrapper(IWorkFlowEngine engine, PaperlessReviewUseCaseSettings settings) {
            _engine = engine;
            _settings = settings;
        }

        public async Task<string?> TryStartRandomEntityAsync(string sourceUseCase, CancellationToken ct) {
            var timeout = _settings.ConfirmationTimeout;
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
            var trigger = await _engine.TriggerAsync(new LifeCycleTriggerRequest {
                EnvCode = _settings.EnvCode,
                DefName = _settings.DefName,
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

        private static string PickEvent(string? preferred, string fallback)
            => !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;

        private async Task<AckOutcome> ConfirmAndTriggerAsync(
            ILifeCycleHookEvent evt,
            ConsumerContext ctx,
            string decisionMessage,
            string yesEventCode,
            string? noEventCode) {
            var timeout = _settings.ConfirmationTimeout;
            var timeoutText = timeout > TimeSpan.Zero ? $", auto-yes in {timeout.TotalSeconds:0}s" : string.Empty;
            var prompt = $"[CONSUMER] {decisionMessage} entity={evt.EntityId} route={evt.Route} (Y/N, Enter=Y{timeoutText})";
            var yes = await AskConfirmationAsync(prompt, ConsoleKey.Y, timeout, ctx.CancellationToken);

            if (yes) {
                return await TriggerNextAsync(evt, ctx, yesEventCode);
            }

            if (!string.IsNullOrWhiteSpace(noEventCode)) {
                return await TriggerNextAsync(evt, ctx, noEventCode);
            }

            Console.WriteLine($"[CONSUMER] route={evt.Route} -> user chose NO, leaving ack as RETRY.");
            return AckOutcome.Retry;
        }

        private static async Task<bool> AskConfirmationAsync(
            string message,
            ConsoleKey defaultKey,
            TimeSpan timeout,
            CancellationToken ct) {
            await PromptLock.WaitAsync(ct);
            try {
                return await ReadConfirmationWithTimeoutAsync(message, defaultKey, timeout, ct);
            } finally {
                PromptLock.Release();
            }
        }

        private static async Task<bool> ReadConfirmationWithTimeoutAsync(
            string message,
            ConsoleKey defaultKey,
            TimeSpan timeout,
            CancellationToken ct) {
            Console.WriteLine();
            Console.WriteLine(message);

            var timeoutEnabled = timeout > TimeSpan.Zero;
            var deadline = DateTime.UtcNow + timeout;

            while (true) {
                ct.ThrowIfCancellationRequested();

                bool hasKey;
                try {
                    hasKey = Console.KeyAvailable;
                } catch (InvalidOperationException) {
                    return defaultKey == ConsoleKey.Y;
                }

                if (hasKey) {
                    var key = Console.ReadKey(intercept: true);
                    Console.WriteLine();

                    if (key.Key == ConsoleKey.Enter) {
                        return defaultKey == ConsoleKey.Y;
                    }

                    if (key.Key == ConsoleKey.Y) {
                        return true;
                    }

                    if (key.Key == ConsoleKey.N) {
                        return false;
                    }

                    Console.WriteLine("Wrong input. Accepted inputs: Y, N, Enter.");
                    continue;
                }

                if (timeoutEnabled && DateTime.UtcNow >= deadline) {
                    Console.WriteLine($"[PROMPT] No input in {timeout.TotalSeconds:0}s. Defaulting to YES.");
                    return defaultKey == ConsoleKey.Y;
                }

                await Task.Delay(100, ct);
            }
        }

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
