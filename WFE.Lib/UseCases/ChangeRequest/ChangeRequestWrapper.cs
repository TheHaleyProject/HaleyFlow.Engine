using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.ChangeRequest {
    [LifeCycleDefinition(ChangeRequestUseCaseSettings.DefinitionNameConst)]
    public sealed class ChangeRequestWrapper : LifeCycleWrapper {
        private static readonly SemaphoreSlim PromptLock = new(1, 1);

        private readonly IWorkFlowEngine _engine;
        private readonly ChangeRequestUseCaseSettings _settings;

        public ChangeRequestWrapper(IWorkFlowEngine engine, ChangeRequestUseCaseSettings settings) {
            _engine = engine;
            _settings = settings;
        }

        public async Task<string?> TryStartRandomEntityAsync(string sourceUseCase, CancellationToken ct) {
            var timeout = _settings.ConfirmationTimeout;
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
            var trigger = await _engine.TriggerAsync(new LifeCycleTriggerRequest {
                EnvCode = _settings.EnvCode,
                DefName = _settings.DefName,
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
                PickEvent(evt.OnFailureEvent, "4002"));

        [HookHandler("APP.CHANGE.COST.REVIEW")]
        private Task<AckOutcome> OnCostReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Cost review approved?",
                PickEvent(evt.OnSuccessEvent, "4003"),
                PickEvent(evt.OnFailureEvent, "4004"));

        [HookHandler("APP.CHANGE.SCHEDULE.REVIEW")]
        private Task<AckOutcome> OnScheduleReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Schedule review approved?",
                PickEvent(evt.OnSuccessEvent, "4005"),
                PickEvent(evt.OnFailureEvent, "4006"));

        [HookHandler("APP.CHANGE.STEERING.DECIDE")]
        private Task<AckOutcome> OnSteeringDecisionAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Steering decision approved?",
                PickEvent(evt.OnSuccessEvent, "4007"),
                PickEvent(evt.OnFailureEvent, "4008"));

        [HookHandler("APP.CHANGE.REWORK.REQUEST")]
        private Task<AckOutcome> OnReworkRequestedAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Submit rework now?",
                PickEvent(evt.OnSuccessEvent, "4009"),
                null);

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

            // Capture runtime: mark this activity as Running before user input.
            await UpsertRuntimeStatusAsync(
                evt,
                "Running",
                new { route = evt.Route, entity = evt.EntityId, question = decisionMessage },
                ctx.CancellationToken);

            var yes = await AskConfirmationAsync(prompt, ConsoleKey.Y, timeout, ctx.CancellationToken);

            if (yes) {
                await UpsertRuntimeStatusAsync(
                    evt,
                    "Approved",
                    new { route = evt.Route, entity = evt.EntityId, decision = "yes" },
                    ctx.CancellationToken);
                return await TriggerNextAsync(evt, ctx, yesEventCode);
            }

            if (!string.IsNullOrWhiteSpace(noEventCode)) {
                await UpsertRuntimeStatusAsync(
                    evt,
                    "Rejected",
                    new { route = evt.Route, entity = evt.EntityId, decision = "no" },
                    ctx.CancellationToken);
                return await TriggerNextAsync(evt, ctx, noEventCode);
            }

            await UpsertRuntimeStatusAsync(
                evt,
                "Retry",
                new { route = evt.Route, entity = evt.EntityId, decision = "retry" },
                ctx.CancellationToken);
            Console.WriteLine($"[CONSUMER] route={evt.Route} -> user chose NO, leaving ack as RETRY.");
            return AckOutcome.Retry;
        }

        private Task<long> UpsertRuntimeStatusAsync(
            ILifeCycleHookEvent evt,
            string status,
            object? data,
            CancellationToken ct) {
            return _engine.UpsertRuntimeAsync(new RuntimeLogByNameRequest {
                Instance = new LifeCycleInstanceKey { InstanceGuid = evt.InstanceGuid },
                Activity = evt.Route,
                Status = status,
                ActorId = "wfe.test.consumer",
                AckGuid = evt.AckGuid,
                Data = data ?? new { }
            }, ct);
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
