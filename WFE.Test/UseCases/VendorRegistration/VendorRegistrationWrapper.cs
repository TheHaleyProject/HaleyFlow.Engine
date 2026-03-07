using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.VendorRegistration {
    [LifeCycleDefinition(VendorRegistrationUseCaseSettings.DefinitionNameConst)]
    internal sealed class VendorRegistrationWrapper : LifeCycleWrapper {
        private readonly IWorkFlowEngine _engine;
        private readonly VendorRegistrationUseCaseSettings _settings;

        public VendorRegistrationWrapper(IWorkFlowEngine engine, VendorRegistrationUseCaseSettings settings) {
            _engine = engine;
            _settings = settings;
        }

        [HookHandler("APP.REG.CHECK.VENDOR_REGISTERED")]
        private Task<AckOutcome> OnCheckVendorRegisteredAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, "1002");

        [HookHandler("APP.REG.CHECK.RECENT_SUBMISSION")]
        private Task<AckOutcome> OnCheckRecentSubmissionAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, "1004");

        [HookHandler("APP.REG.PQ.VALIDATION.START")]
        private Task<AckOutcome> OnValidationStartAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, evt.OnSuccessEvent ?? "1005");

        [HookHandler("APP.REG.PQ.VALIDATION.REQUIRED")]
        private Task<AckOutcome> OnValidationRequiredAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, evt.OnSuccessEvent ?? "1006");

        [HookHandler("APP.REG.VENDOR_CREATE")]
        private Task<AckOutcome> OnVendorCreateAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => TriggerNextAsync(evt, ctx, evt.OnSuccessEvent ?? "1007");

        protected override Task<AckOutcome> OnUnhandledTransitionAsync(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] transition event={evt.EventCode} state={evt.FromStateId}->{evt.ToStateId} (no custom action)");
            return Task.FromResult(AckOutcome.Processed);
        }

        protected override Task<AckOutcome> OnUnhandledHookAsync(ILifeCycleHookEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] hook route={evt.Route} (no custom action)");
            return Task.FromResult(AckOutcome.Processed);
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
