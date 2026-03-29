using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.ChangeRequest {
    [LifeCycleDefinition(DefinitionNameConst)]
    public sealed class ChangeRequestWrapper : InteractiveHookWrapperBase {
        public const string DefinitionNameConst = "ProjectChangeRequest";
        private const int ACTION_IMPACT_AUDIT_LOG = 4101;
        private const int ACTION_IMPACT_NOTIFY_REQUESTOR = 4102;
        private const int ACTION_COST_LOCAL_BACKUP = 4103;
        private const int ACTION_COST_EMAIL_STAKEHOLDERS = 4104;
        private const int ACTION_SCHEDULE_AUDIT_LOG = 4105;
        private const int ACTION_SCHEDULE_CALENDAR_UPDATE = 4106;
        private const int ACTION_STEERING_MINUTES_ARCHIVE = 4107;
        private const int ACTION_STEERING_NOTIFY_WATCHERS = 4108;
        private const int ACTION_REWORK_LOCAL_BACKUP = 4109;
        private const int ACTION_REWORK_EMAIL_REQUESTOR = 4110;
        private const int ACTION_REWORK_EXPIRED = 4111;
        private const int ACTION_REWORK_EXPIRED_NOTIFY = 4112;

        protected override string DefinitionName => DefinitionNameConst;

        public ChangeRequestWrapper(UseCaseRuntimeOptions options)
            : base(options) { }

        public async Task<string?> TryStartRandomEntityAsync(string sourceUseCase, CancellationToken ct) {
            var timeout = Options.ConfirmationTimeout;
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
            var trigger = await Engine.TriggerAsync(new LifeCycleTriggerRequest {
                EnvCode = Options.EnvCode,
                DefName = DefinitionNameConst,
                EntityId = entityId,
                Event = "4000",
                Actor = "wfe.test.wrapper-driver",
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
            => ConfirmAndAcknowledgeAsync(
                evt,
                ctx,
                "Impact assessment approved?",
                PickEvent(evt.OnSuccessEvent, "4001"),
                PickEvent(evt.OnFailureEvent, "4002"),
                ResolveExecutionModeFromParams(evt));

        [HookHandler("APP.CHANGE.IMPACT.AUDIT.LOG")]
        private Task<AckOutcome> OnImpactAuditLogAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_IMPACT_AUDIT_LOG, "Impact audit log");

        [HookHandler("APP.CHANGE.IMPACT.NOTIFY.REQUESTOR")]
        private Task<AckOutcome> OnImpactNotifyRequestorAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_IMPACT_NOTIFY_REQUESTOR, "Impact requestor notification");

        [HookHandler("APP.CHANGE.COST.REVIEW")]
        private Task<AckOutcome> OnCostReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndAcknowledgeAsync(
                evt,
                ctx,
                "Cost review approved?",
                PickEvent(evt.OnSuccessEvent, "4003"),
                PickEvent(evt.OnFailureEvent, "4004"),
                BusinessActionExecutionMode.ForceRun);

        [HookHandler("APP.CHANGE.COST.LOCAL.BACKUP")]
        private Task<AckOutcome> OnCostLocalBackupAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_COST_LOCAL_BACKUP, "Cost review backup");

        [HookHandler("APP.CHANGE.COST.EMAIL.STAKEHOLDERS")]
        private Task<AckOutcome> OnCostEmailStakeholdersAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_COST_EMAIL_STAKEHOLDERS, "Cost review stakeholder email");

        [HookHandler("APP.CHANGE.SCHEDULE.REVIEW")]
        private Task<AckOutcome> OnScheduleReviewAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndAcknowledgeAsync(
                evt,
                ctx,
                "Schedule review approved?",
                PickEvent(evt.OnSuccessEvent, "4005"),
                PickEvent(evt.OnFailureEvent, "4006"),
                BusinessActionExecutionMode.ForceRun);

        [HookHandler("APP.CHANGE.SCHEDULE.AUDIT.LOG")]
        private Task<AckOutcome> OnScheduleAuditLogAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_SCHEDULE_AUDIT_LOG, "Schedule audit log");

        [HookHandler("APP.CHANGE.SCHEDULE.CALENDAR.UPDATE")]
        private Task<AckOutcome> OnScheduleCalendarUpdateAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_SCHEDULE_CALENDAR_UPDATE, "Project calendar update");

        [HookHandler("APP.CHANGE.STEERING.DECIDE")]
        private Task<AckOutcome> OnSteeringDecisionAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndAcknowledgeAsync(
                evt,
                ctx,
                "Steering decision approved?",
                PickEvent(evt.OnSuccessEvent, "4007"),
                PickEvent(evt.OnFailureEvent, "4008"),
                BusinessActionExecutionMode.ForceRun);

        [HookHandler("APP.CHANGE.STEERING.MINUTES.ARCHIVE")]
        private Task<AckOutcome> OnSteeringMinutesArchiveAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_STEERING_MINUTES_ARCHIVE, "Steering minutes archive");

        [HookHandler("APP.CHANGE.STEERING.NOTIFY.WATCHERS")]
        private Task<AckOutcome> OnSteeringNotifyWatchersAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_STEERING_NOTIFY_WATCHERS, "Steering watcher notification");

        [HookHandler("APP.CHANGE.REWORK.REQUEST")]
        private Task<AckOutcome> OnReworkRequestedAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => ConfirmAndAcknowledgeAsync(
                evt,
                ctx,
                "Submit rework now?",
                PickEvent(evt.OnSuccessEvent, "4009"),
                null,
                BusinessActionExecutionMode.SkipIfCompleted);

        [HookHandler("APP.CHANGE.REWORK.LOCAL.BACKUP")]
        private Task<AckOutcome> OnReworkLocalBackupAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_REWORK_LOCAL_BACKUP, "Rework backup");

        [HookHandler("APP.CHANGE.REWORK.EMAIL.REQUESTOR")]
        private Task<AckOutcome> OnReworkEmailRequestorAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_REWORK_EMAIL_REQUESTOR, "Rework requestor email");

        [HookHandler("APP.CHANGE.REWORK.EXPIRED")]
        private Task<AckOutcome> OnReworkExpiredAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_REWORK_EXPIRED, "Rework expiry processing");

        [HookHandler("APP.CHANGE.REWORK.EXPIRED.NOTIFY")]
        private Task<AckOutcome> OnReworkExpiredNotifyAsync(ILifeCycleHookEvent evt, ConsumerContext ctx)
            => HandleSideEffectAsync(evt, ctx, ACTION_REWORK_EXPIRED_NOTIFY, "Rework expiry notification");

        [TransitionHandler(4000)]
        private Task<AckOutcome> OnAutoStartTransitionAsync(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
            Console.WriteLine($"[CONSUMER] transition-handler event={evt.EventCode} entity={evt.EntityId} state={evt.FromStateId}->{evt.ToStateId}");
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

        protected override int? ResolveTransitionCompleteFallbackEvent(ILifeCycleCompleteEvent evt, ConsumerContext ctx) => null;

        protected override Task BeforeDecisionAsync(ILifeCycleHookEvent evt, string decisionMessage, CancellationToken ct)
            => UpsertRuntimeStatusAsync(
                evt,
                "Running",
                new { route = evt.Route, entity = evt.EntityId, question = decisionMessage },
                ct);

        protected override Task AfterDecisionAsync(ILifeCycleHookEvent evt, bool yes, bool hasFailurePath, CancellationToken ct)
            => UpsertRuntimeStatusAsync(
                evt,
                yes ? "Approved" : (hasFailurePath ? "Rejected" : "Retry"),
                new { route = evt.Route, entity = evt.EntityId, decision = yes ? "yes" : (hasFailurePath ? "no" : "retry") },
                ct);

        // Side-effect hooks are intentionally idempotent. If the same ack is re-delivered,
        // BusinessAction prevents us from replaying backup/email/log work for the same entity.
        private async Task<AckOutcome> HandleSideEffectAsync(ILifeCycleHookEvent evt, ConsumerContext ctx, int actionCode, string actionName) {
            var execution = await ExecuteBusinessActionAsync(
                ctx,
                actionCode,
                async token => {
                    Console.WriteLine($"[CONSUMER] side-effect route={evt.Route} action={actionName} entity={evt.EntityId}");
                    await UpsertRuntimeStatusAsync(
                        evt,
                        "Processed",
                        new { route = evt.Route, entity = evt.EntityId, action = actionName },
                        token);
                    return new { route = evt.Route, entity = evt.EntityId, action = actionName };
                },
                BusinessActionExecutionMode.SkipIfCompleted);

            if (!execution.Executed) {
                Console.WriteLine($"[CONSUMER] side-effect route={evt.Route} already completed; reusing prior result.");
            }

            return AckOutcome.Processed;
        }

        private static BusinessActionExecutionMode ResolveExecutionModeFromParams(ILifeCycleHookEvent evt) {
            // Example override:
            // params: { "forceBusinessAction": true } => ForceRun
            if (evt.Params == null || evt.Params.Count < 1) return BusinessActionExecutionMode.SkipIfCompleted;
            for (var i = 0; i < evt.Params.Count; i++) {
                var data = evt.Params[i]?.Data;
                if (data == null || data.Count < 1) continue;
                if (TryReadBool(data, "forceRerun", out var force) && force) {
                    return BusinessActionExecutionMode.ForceRun;
                }
            }
            return BusinessActionExecutionMode.SkipIfCompleted;
        }

        private static bool TryReadBool(IReadOnlyDictionary<string, object?> data, string key, out bool value) {
            value = false;
            if (!data.TryGetValue(key, out var raw) || raw == null) return false;

            switch (raw) {
                case bool b:
                    value = b;
                    return true;
                case string s when bool.TryParse(s, out var parsed):
                    value = parsed;
                    return true;
                case int i:
                    value = i != 0;
                    return true;
                case long l:
                    value = l != 0;
                    return true;
                case JsonElement je when je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.False:
                    value = je.GetBoolean();
                    return true;
                case JsonElement je when je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var p2):
                    value = p2;
                    return true;
                case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var p3):
                    value = p3 != 0;
                    return true;
                default:
                    return false;
            }
        }

        private async Task<long> UpsertRuntimeStatusAsync(
            ILifeCycleHookEvent evt,
            string status,
            object? data,
            CancellationToken ct) {
            return await Engine.UpsertRuntimeAsync(new RuntimeLogByNameRequest {
                Instance = new LifeCycleInstanceKey { InstanceGuid = evt.InstanceGuid },
                Activity = evt.Route,
                Status = status,
                ActorId = "wfe.test.consumer",
                AckGuid = evt.AckGuid,
                Data = data ?? new { }
            }, ct);
        }
    }
}
