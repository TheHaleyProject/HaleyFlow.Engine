using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.LoanApproval {
    [LifeCycleDefinition(LoanApprovalUseCaseSettings.DefinitionNameConst)]
    internal sealed class LoanApprovalWrapper : LifeCycleWrapper {
        private readonly IWorkFlowEngine _engine;
        private readonly LoanApprovalUseCaseSettings _settings;

        public LoanApprovalWrapper(IWorkFlowEngine engine, LoanApprovalUseCaseSettings settings) {
            _engine = engine;
            _settings = settings;
        }

        [HookHandler("APP.LOAN.KYC.CHECK")]
        private Task<AckOutcome> OnKycCheckAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "2001"));

        [HookHandler("APP.LOAN.CREDIT.CHECK")]
        private Task<AckOutcome> OnCreditCheckAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "2003"));

        [HookHandler("APP.LOAN.RISK.CHECK")]
        private Task<AckOutcome> OnRiskCheckAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "2005"));

        [HookHandler("APP.LOAN.MANAGER.DECISION")]
        private Task<AckOutcome> OnManagerDecisionAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, PickEvent(evt.OnSuccessEvent, "2007"));

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
