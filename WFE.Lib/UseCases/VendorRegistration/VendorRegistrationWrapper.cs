using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.VendorRegistration {
    [LifeCycleDefinition(VendorRegistrationUseCaseSettings.DefinitionNameConst)]
    public sealed class VendorRegistrationWrapper : LifeCycleWrapper {
        private static readonly SemaphoreSlim PromptLock = new(1, 1);

        private readonly IWorkFlowEngine _engine;
        private readonly VendorRegistrationUseCaseSettings _settings;

        public VendorRegistrationWrapper(IWorkFlowEngine engine, VendorRegistrationUseCaseSettings settings) {
            _engine = engine;
            _settings = settings;
        }

        public async Task<string?> TryStartRandomEntityAsync(string sourceUseCase, CancellationToken ct) {
            var timeout = _settings.ConfirmationTimeout;
            var timeoutText = timeout > TimeSpan.Zero ? $", auto-yes in {timeout.TotalSeconds:0}s" : string.Empty;
            var createNow = await AskConfirmationAsync(
                $"[DRIVER] Create a new random vendor registration entity now? (Y/N, Enter=Y{timeoutText})",
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
                Event = "1000",
                Actor = "wfe.test.wrapper-driver",
                AckRequired = true,
                Payload = new Dictionary<string, object> {
                    ["source"] = "WFE.Test",
                    ["useCase"] = sourceUseCase,
                    ["driver"] = nameof(VendorRegistrationWrapper)
                },
                Metadata = "debug;bulk-seed;vendor-registration"
            }, ct);

            Console.WriteLine($"[DRIVER] entity={entityId} startEvent=1000 applied={trigger.Applied} instanceId={trigger.InstanceId} reason={trigger.Reason}");
            return trigger.Applied ? entityId : null;
        }

        [HookHandler("APP.REG.CHECK.VENDOR_REGISTERED")]
        private Task<AckOutcome> OnCheckVendorRegisteredAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Vendor already registered?",
                PickEvent(evt.OnSuccessEvent, "1001"),
                PickEvent(evt.OnFailureEvent, "1002"));

        [HookHandler("APP.REG.CHECK.RECENT_SUBMISSION")]
        private Task<AckOutcome> OnCheckRecentSubmissionAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Recent submission found?",
                PickEvent(evt.OnSuccessEvent, "1003"),
                PickEvent(evt.OnFailureEvent, "1004"));

        [HookHandler("APP.REG.PQ.VALIDATION.START")]
        private Task<AckOutcome> OnValidationStartAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Begin PQ validation now?",
                PickEvent(evt.OnSuccessEvent, "1005"),
                null);

        [HookHandler("APP.REG.PQ.VALIDATION.REQUIRED")]
        private Task<AckOutcome> OnValidationRequiredAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Company is valid?",
                PickEvent(evt.OnSuccessEvent, "1006"),
                PickEvent(evt.OnFailureEvent, "1008"));

        [HookHandler("APP.REG.PQ.OVERDUE.ENTER")]
        private Task<AckOutcome> OnOverdueEnterAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Overdue entered. Resume validation now?",
                PickEvent(evt.OnSuccessEvent, "1012"),
                null);

        [HookHandler("APP.REG.PQ.OVERDUE.REMIND")]
        private Task<AckOutcome> OnOverdueRemindAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Overdue reminder received. Resume validation now?",
                PickEvent(evt.OnSuccessEvent, "1012"),
                null);

        [HookHandler("APP.REG.VENDOR_CREATE")]
        private Task<AckOutcome> OnVendorCreateAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Vendor creation successful?",
                PickEvent(evt.OnSuccessEvent, "1007"),
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
