using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.VendorRegistration {
    [LifeCycleDefinition(DefinitionNameConst)]
    public sealed class VendorRegistrationWrapper : InteractiveHookWrapperBase {
        public const string DefinitionNameConst = "VendorRegistration";
        protected override string DefinitionName => DefinitionNameConst;

        public VendorRegistrationWrapper(UseCaseRuntimeOptions options)
            : base(options) { }

        public async Task<string?> TryStartRandomEntityAsync(string sourceUseCase, CancellationToken ct) {
            var timeout = Options.ConfirmationTimeout;
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
            var trigger = await Engine.TriggerAsync(new LifeCycleTriggerRequest {
                EnvCode = Options.EnvCode,
                DefName = DefinitionNameConst,
                EntityId = entityId,
                Event = "1000",
                Actor = "wfe.test.wrapper-driver",
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

        protected override int? ResolveTransitionCompleteFallbackEvent(ILifeCycleCompleteEvent evt, ConsumerContext ctx) => null;
    }
}
