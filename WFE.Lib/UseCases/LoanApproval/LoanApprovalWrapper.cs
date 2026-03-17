using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.LoanApproval {
    [LifeCycleDefinition(DefinitionNameConst)]
    public sealed class LoanApprovalWrapper : InteractiveHookWrapperBase {
        public const string DefinitionNameConst = "LoanApproval";
        protected override string DefinitionName => DefinitionNameConst;

        public LoanApprovalWrapper(UseCaseRuntimeOptions options)
            : base(options) { }

        public async Task<string?> TryStartRandomEntityAsync(string sourceUseCase, CancellationToken ct) {
            var timeout = Options.ConfirmationTimeout;
            var timeoutText = timeout > TimeSpan.Zero ? $", auto-yes in {timeout.TotalSeconds:0}s" : string.Empty;
            var createNow = await AskConfirmationAsync(
                $"[DRIVER] Create a new random loan approval entity now? (Y/N, Enter=Y{timeoutText})",
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
                Event = "2000",
                Actor = "wfe.test.wrapper-driver",
                Payload = new Dictionary<string, object> {
                    ["source"] = "WFE.Test",
                    ["useCase"] = sourceUseCase,
                    ["driver"] = nameof(LoanApprovalWrapper)
                },
                Metadata = "debug;bulk-seed;loan-approval"
            }, ct);

            Console.WriteLine($"[DRIVER] entity={entityId} startEvent=2000 applied={trigger.Applied} instanceId={trigger.InstanceId} reason={trigger.Reason}");
            return trigger.Applied ? entityId : null;
        }

        [HookHandler("APP.LOAN.KYC.CHECK")]
        private Task<AckOutcome> OnKycCheckAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "KYC passed?",
                PickEvent(evt.OnSuccessEvent, "2001"),
                PickEvent(evt.OnFailureEvent, "2002"));

        [HookHandler("APP.LOAN.CREDIT.CHECK")]
        private Task<AckOutcome> OnCreditCheckAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Credit assessment accepted?",
                PickEvent(evt.OnSuccessEvent, "2003"),
                PickEvent(evt.OnFailureEvent, "2004"));

        [HookHandler("APP.LOAN.RISK.CHECK")]
        private Task<AckOutcome> OnRiskCheckAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Risk review accepted?",
                PickEvent(evt.OnSuccessEvent, "2005"),
                PickEvent(evt.OnFailureEvent, "2006"));

        [HookHandler("APP.LOAN.MANAGER.DECISION")]
        private Task<AckOutcome> OnManagerDecisionAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndTriggerAsync(
                evt,
                ctx,
                "Manager approved loan?",
                PickEvent(evt.OnSuccessEvent, "2007"),
                PickEvent(evt.OnFailureEvent, "2008"));

        [HookHandler("APP.LOAN.MANAGER.REMINDER")]
        private Task<AckOutcome> OnManagerReminderAsync(ILifeCycleHookEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] reminder route={evt.Route} entity={evt.EntityId} (no follow-up trigger).");
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
