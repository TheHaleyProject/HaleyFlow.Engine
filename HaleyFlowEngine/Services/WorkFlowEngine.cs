using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    // WorkFlowEngine is the central brain of the lifecycle system.
    // It is NOT created directly by the caller — the builder (WorkFlowEngineMaker) constructs it after
    // verifying and provisioning the DB schema. The caller only ever sees ILifeCycleEngine.
    //
    // Internally it is composed of specialised sub-engines (all internal, not exposed publicly):
    //   BlueprintManager  — caches and resolves definitions+versions+states+events by envCode+defName
    //   BlueprintImporter — writes definition JSON and policy JSON into the DB (idempotent, hash-guarded)
    //   StateMachine      — applies state transitions and writes lifecycle (lc_data) entries
    //   PolicyEnforcer    — evaluates policy rules; decides which hooks to emit per transition
    //   AckManager        — creates ACK entries, schedules resends, tracks delivery per consumer
    //   RuntimeEngine     — persists arbitrary runtime-log entries per activity/actor (side-channel data)
    //   Monitor           — background timer that drives the resend loop for undelivered events
    public sealed class WorkFlowEngine : IWorkFlowEngine {
        public IDALUtilBase Dal => _dal;
        private readonly IWorkFlowDAL _dal;
        private readonly WorkFlowEngineOptions _opt;
        internal IStateMachine StateMachine { get; }
        internal IBlueprintManager BlueprintManager { get; }
        internal IBlueprintImporter BlueprintImporter { get; }
        internal IPolicyEnforcer PolicyEnforcer { get; }
        internal IAckManager AckManager { get; }
        internal IRuntimeEngine Runtime { get; }
        internal ILifeCycleMonitor Monitor { get; }

        // Two public events that the host application subscribes to:
        //   EventRaised  — a lifecycle transition or hook event that a consumer must process and ACK.
        //   NoticeRaised — informational / warning / error signals (ACK_RETRY, STATE_STALE, etc.) for monitoring.
        //                  These are fire-and-forget signals; they don't affect engine state.
        public event Func<ILifeCycleEvent, Task>? EventRaised;
        public event Func<LifeCycleNotice, Task>? NoticeRaised;

        // In-memory throttle map for STATE_STALE notices — prevents the same (consumer, instance, state)
        // combination from flooding the NoticeRaised handler on every monitor tick.
        readonly ConcurrentDictionary<string, DateTimeOffset> _overDueLastAt = new(StringComparer.Ordinal);

        internal WorkFlowEngine(IWorkFlowDAL dal, WorkFlowEngineOptions? options = null) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _opt = options ?? new WorkFlowEngineOptions();

            BlueprintManager = new BlueprintManager(_dal);
            BlueprintImporter = new BlueprintImporter(_dal);
            StateMachine = new StateMachine(_dal, BlueprintManager);
            PolicyEnforcer =  new PolicyEnforcer(_dal);

            // WHY ResolveConsumers is a callback and not a DB query:
            //
            // The engine stores consumer rows (via RegisterConsumerAsync) and knows they are alive (via
            // BeatConsumerAsync), but it has NO knowledge of WHICH consumer handles WHICH definition.
            // That mapping is entirely application-level — and intentionally so.
            //
            // Think of the real world: one environment might have 20 definitions (loan-approval, mortgage,
            // onboarding, ...) and 5 consumer processes. Each process only cares about 2-3 definitions.
            // A consumer doesn't register for all definitions — it subscribes only to the ones it handles.
            //
            // This is the responsibility of Haley.Flow.Hub (the orchestration layer that sits above the engine):
            //   - Hub knows every consumer's subscriptions: consumer-A → [loan-approval, onboarding]
            //   - When the engine asks "for defVersionId X, give me the consumer IDs", Hub answers that question
            //   - The engine then uses those IDs to create ack_consumer rows → fan-out events → track delivery
            //
            // The callback signature is:
            //   Func<LifeCycleConsumerType, long? defVersionId, CancellationToken, Task<IReadOnlyList<long>>>
            //
            // LifeCycleConsumerType distinguishes what kind of consumers are needed:
            //   Transition — consumer IDs that should receive lifecycle transition events for this def
            //   Hook       — consumer IDs that should receive hook events for this def (may differ)
            //   Monitor    — consumer IDs the monitor should run resends for (defVersionId is null here —
            //                the monitor just needs "all active consumers", not filtered by definition)
            //
            // If ResolveConsumers is not supplied, TriggerAsync will throw (no consumers = nothing to fan-out to).
            var resolveConsumers = _opt.ResolveConsumers ?? ((LifeCycleConsumerType ty, long? id /* DefVersionId */, CancellationToken ct) => Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>()));

            // Monitor uses the same callback but without a defVersionId — it wants ALL active consumers
            // so it can resend overdue ACK rows for any of them, regardless of which definition they belong to.
            var resolveMonitors = (LifeCycleConsumerType ty, CancellationToken ct) => resolveConsumers.Invoke(ty, null, ct);

            AckManager = new AckManager(_dal, BlueprintManager, PolicyEnforcer, resolveConsumers, _opt.AckPendingResendAfter, _opt.AckDeliveredResendAfter);

            Runtime = new RuntimeEngine(_dal);

            // The monitor is a background periodic loop. Every MonitorInterval it:
            //   1. Scans for stale instances that have been sitting in the same state too long (STATE_STALE notices)
            //   2. For each active consumer, resends any overdue Pending or Delivered ACK rows
            // Any uncaught exception fires a MONITOR_ERROR notice — the loop itself never crashes the process.
            Monitor = new LifeCycleMonitor(_opt.MonitorInterval, ct => RunMonitorOnceInternalAsync(resolveMonitors, ct), (ex) => FireNotice(LifeCycleNotice.Error("MONITOR_ERROR", "MONITOR_ERROR", ex.Message, ex)));
        }

        public Task StartMonitorAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Monitor.StartAsync(ct); }

        public Task StopMonitorAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Monitor.StopAsync(ct); }

        public async ValueTask DisposeAsync() { try { await StopMonitorAsync(CancellationToken.None); } catch { } await Monitor.DisposeAsync(); await _dal.DisposeAsync(); }

        // -----------------------------------------------------------------------
        // TRIGGER — the main entry point. The caller says: "entity X just triggered event Y on definition Z."
        //
        // Everything happens in a single atomic transaction:
        //   1. Load the blueprint (definition + all states/events) — from cache, no DB round-trip usually
        //   2. Ask ResolveConsumers who should receive transition events (throws if nobody)
        //   3. Ensure the instance exists (creates it on first call); attach the current policy
        //   4. If instance is on an old def_version, reload blueprint for that locked version
        //   5. ACK gate check — optionally block if a prior transition still has unresolved consumers
        //   6. Apply the state machine transition → write lc_data row, update instance.current_state
        //   7. Create lifecycle ACK rows (one ack guid, one ack_consumer row per consumer)
        //   8. Evaluate policy hooks for the target state → create hook rows + hook ACK rows
        //   9. Commit all writes atomically
        //  10. Fire events AFTER commit — fire-and-forget; the monitor handles missed deliveries
        // -----------------------------------------------------------------------
        public async Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrWhiteSpace(req.DefName)) throw new ArgumentNullException(nameof(req.DefName));
            if (string.IsNullOrWhiteSpace(req.EntityId)) throw new ArgumentNullException(nameof(req.EntityId));
            if (string.IsNullOrWhiteSpace(req.Event)) throw new ArgumentNullException(nameof(req.Event));

            // Blueprint read can be outside txn (pure read + cached).
            var bp = await BlueprintManager.GetBlueprintLatestAsync(req.EnvCode, req.DefName, ct);

            // Transition consumers must be resolved before we open the transaction. We need at least one
            // consumer to receive this event — otherwise the ACK rows would be orphaned with no one to deliver to.
            // Hook consumers are resolved later (after transition succeeds) because hooks are optional.
            var transitionConsumers = await AckManager.GetTransitionConsumersAsync(bp.DefVersionId, ct);
            if (transitionConsumers.Count < 1) throw new ArgumentException("No transition consumers found for this definition version. At least one transition consumer is required to proceed.", nameof(req));

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;

            var toDispatch = new List<ILifeCycleEvent>(8);
            var lcAckGuids = new List<string>(4);
            var hookAckGuids = new List<string>(8);

            DbRow instance = null!;
            ApplyTransitionResult transition = null!;
            PolicyResolution pr = null!;

            try {
                // Resolve the LATEST policy for this definition. This is only used for new instances —
                // once the policy is attached to an instance it is locked. The policy stores rule conditions
                // (which hooks fire, on_success/on_failure event codes, params) that drive the workflow logic.
                // If the policy changes tomorrow, already-running instances are unaffected.
                var policy = await PolicyEnforcer.ResolvePolicyAsync(bp.DefinitionId, load); //latest policy
                instance = await StateMachine.EnsureInstanceAsync(bp.DefVersionId, req.EntityId, policy.PolicyId ?? 0, req.Metadata, load);

                // Version lock: once an instance is created, it is tied to the def_version at that moment.
                // If the definition was later updated (new states added, transitions changed), existing instances
                // continue using their original version. We reload the blueprint for that locked version here
                // so all subsequent state/event lookups are consistent with what this instance was created under.
                var instanceDefVersion = instance.GetLong("def_version");
                if (instanceDefVersion != bp.DefVersionId) {
                    bp = await BlueprintManager.GetBlueprintByVersionIdAsync(instanceDefVersion, ct);
                }

                // ACK gate: if enabled, block this transition if any consumer for the LAST lifecycle entry
                // still hasn't ACKed (status is Pending or Delivered). The idea is: don't advance the state
                // machine if the current event hasn't been fully processed yet.
                // The caller can bypass this with SkipAckGate=true (e.g. for administrative corrections).
                if (_opt.AckGateEnabled && !req.SkipAckGate) {
                    var gateInstanceId = instance.GetLong("id");
                    var pendingAckCount = await _dal.LcAck.CountPendingForInstanceAsync(gateInstanceId, load);
                    if (pendingAckCount > 0) {
                        transaction.Commit();
                        committed = true;
                        return new LifeCycleTriggerResult {
                            Applied = false,
                            InstanceGuid = instance.GetString("guid") ?? string.Empty,
                            InstanceId = gateInstanceId,
                            Reason = "BlockedByPendingAck",
                            LifecycleAckGuids = Array.Empty<string>(),
                            HookAckGuids = Array.Empty<string>()
                        };
                    }
                }

                transition = await StateMachine.ApplyTransitionAsync(bp, instance, req.Event, req.Actor, req.Payload, req.OccurredAt, load);

                var result = new LifeCycleTriggerResult {
                    Applied = transition.Applied,
                    InstanceGuid = instance.GetString("guid") ?? string.Empty,
                    InstanceId = instance.GetLong("id"),
                    LifeCycleId = transition.LifeCycleId,
                    FromState = bp.StatesById.TryGetValue(transition.FromStateId, out var fs) ? (fs.Name ?? string.Empty) : string.Empty,
                    ToState = bp.StatesById.TryGetValue(transition.ToStateId, out var ts) ? (ts.Name ?? string.Empty) : string.Empty,
                    Reason = transition.Reason ?? string.Empty,
                    LifecycleAckGuids = Array.Empty<string>(),
                    HookAckGuids = Array.Empty<string>()
                };

                // Even if the transition was not applied (event not valid in current state, or same-state),
                // the instance may have just been CREATED for the first time — commit that creation.
                // The caller will see Applied=false with a Reason explaining why (e.g. "NoTransition").
                if (!transition.Applied) {
                    transaction.Commit(); 
                    committed = true;
                    return result;
                }

                var hookConsumers = await AckManager.GetHookConsumersAsync(bp.DefVersionId, ct);
                var normTransitionConsumers = NormalizeConsumers(transitionConsumers);
                var normHookConsumers = NormalizeConsumers(hookConsumers);

                var instanceId = result.InstanceId;
                // Now reload the policy that is ACTUALLY attached to this instance, not the latest one.
                // The instance might have been created months ago when a different policy was active.
                // We need the right policy to evaluate hooks and resolve params for the target state.
                var pid = instance.GetLong("policy_id");
                if (pid > 0) pr = await PolicyEnforcer.ResolvePolicyByIdAsync(pid, load);

                // Create the lifecycle ACK entry. One ack_guid is shared across all consumers — but each
                // consumer gets its own ack_consumer row so we can track delivery independently.
                // The monitor will resend the event to any consumer whose row is still Pending/Delivered.
                var lcAckGuid = string.Empty;
                if (req.AckRequired) {
                    var ackRef = await AckManager.CreateLifecycleAckAsync(transition.LifeCycleId!.Value, normTransitionConsumers, (int)AckStatus.Pending, load);
                    lcAckGuid = ackRef.AckGuid ?? string.Empty;
                    lcAckGuids.Add(lcAckGuid);
                }

                // Resolve rule context from the policy JSON: for the target state + triggering event,
                // extract the params (data consumers need), and on_success / on_failure event codes
                // (what the consumer should trigger next if its work succeeds or fails).
                RuleContext ctx = new RuleContext();
                if (!string.IsNullOrWhiteSpace(pr.PolicyJson) && bp.StatesById.TryGetValue(transition.ToStateId, out var toState)) {
                    bp.EventsById.TryGetValue(transition.EventId, out var viaEvent);
                    ctx = PolicyEnforcer.ResolveRuleContextFromJson(pr.PolicyJson!, toState, viaEvent, ct);
                }

                // Build the base event object. This is cloned for every consumer (transition + hook).
                // Metadata is the instance-level immutable string set when the instance was first created —
                // it travels on every event so consumers don't need a separate lookup to read it.
                var lcEvent = new LifeCycleEvent() {
                    InstanceGuid = result.InstanceGuid,
                    DefinitionId = bp.DefinitionId,
                    DefinitionVersionId = bp.DefVersionId,
                    EntityId = req.EntityId,
                    OccurredAt = req.OccurredAt ?? DateTimeOffset.UtcNow,
                    AckGuid = lcAckGuid,
                    AckRequired = req.AckRequired,
                    Metadata = instance.GetString("metadata"),
                    Params = ctx.Params,            
                    OnSuccessEvent = ctx.OnSuccessEvent, 
                    OnFailureEvent = ctx.OnFailureEvent 
                };

                // Build one LifeCycleTransitionEvent per consumer. Each copy carries the same data
                // but a different ConsumerId — so consumers identify themselves when calling AckAsync.
                for (var i = 0; i < normTransitionConsumers.Count; i++) {
                    var consumerId = normTransitionConsumers[i];
                    var transitionEvent = new LifeCycleTransitionEvent(lcEvent) {
                        ConsumerId = consumerId,
                        LifeCycleId = transition.LifeCycleId.Value,
                        FromStateId = transition.FromStateId,
                        ToStateId = transition.ToStateId,
                        EventCode = transition.EventCode,
                        EventName = transition.EventName ?? string.Empty,
                        PrevStateMeta = new Dictionary<string, object>()
                    };
                    toDispatch.Add(transitionEvent);
                }

                // Hooks: PolicyEnforcer looks at the policy JSON and decides which hooks should fire
                // for this transition (based on target state, via-event, on_entry/on_exit rules).
                // It creates the hook rows in the DB and returns only the min-order hooks (dispatched=true).
                // Higher-order hooks are created in the DB with dispatched=0 and will be activated later
                // by AdvanceNextHookOrderAsync once the current order's blocking hooks are all Processed.
                var hookEmissions = await PolicyEnforcer.EmitHooksAsync(bp, instance, transition, load, pr?.PolicyJson, req.AckRequired);
                for (var h = 0; h < hookEmissions.Count; h++) {
                    var he = hookEmissions[h];
                   if (hookConsumers.Count < 1) throw new ArgumentException("No Hook consumers found for this definition version. At least one hook consumer is required to proceed.", nameof(req));

                    var hookAckGuid = string.Empty;
                    if (req.AckRequired) {
                        var hookAck = await AckManager.CreateHookAckAsync(he.HookId, normHookConsumers, (int)AckStatus.Pending, load);
                        hookAckGuid = hookAck.AckGuid ?? string.Empty;
                        hookAckGuids.Add(hookAckGuid);
                    }

                    for (var i = 0; i < normHookConsumers.Count; i++) {

                        var consumerId = normHookConsumers[i];
                        var hookEvent = new LifeCycleHookEvent(lcEvent) {
                            ConsumerId = consumerId,
                            AckGuid = hookAckGuid,
                            OnEntry = he.OnEntry,
                            Route = he.Route ?? string.Empty,
                            OnSuccessEvent = he.OnSuccessEvent ?? string.Empty,
                            OnFailureEvent = he.OnFailureEvent ?? string.Empty,
                            Params = he.Params,
                            NotBefore = he.NotBefore,
                            Deadline = he.Deadline,
                            IsBlocking = he.IsBlocking,
                            GroupName = he.GroupName,
                            OrderSeq = he.OrderSeq,
                            AckMode = he.AckMode
                        };
                        toDispatch.Add(hookEvent);
                    }
                }

                transaction.Commit();
                committed = true;

                // Dispatch AFTER commit — this is critical. All ACK rows already exist in the DB,
                // so even if the process crashes here or a handler throws, the monitor will resend
                // on the next tick based on the still-Pending ack_consumer rows.
                await DispatchEventsSafeAsync(toDispatch, ct);

                result.LifecycleAckGuids = lcAckGuids;
                result.HookAckGuids = hookAckGuids;
                return result;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                throw;
            } catch (Exception ex) {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                FireNotice(LifeCycleNotice.Error("TRIGGER_ERROR", "TRIGGER_ERROR", ex.Message, ex));
                throw;
            }
        }

        // -----------------------------------------------------------------------
        // ACK — consumer tells us what happened with an event it received.
        //
        // Outcomes:
        //   Processed — consumer handled the event successfully. Move the ack_consumer row to terminal state.
        //   Retry     — consumer wants the engine to resend later (transient failure, retryAt is honoured).
        //   Dead      — consumer gave up permanently; mark row Failed, do not retry.
        //
        // When Processed, we also run three side-effect checks specifically for hook events:
        //   1. AckMode=Any fan-out  — if hook had ack_mode=Any, auto-mark ALL sibling consumers Processed
        //                             so they don't get retried. "First one to ACK wins."
        //   2. Group completion     — if hook is part of a named group, check if every member is now done.
        //                             If yes, fire HOOK_GROUP_COMPLETE notice.
        //   3. Order advancement    — if hook was blocking, check if ALL blocking hooks in its order_seq
        //                             are now Processed. If yes, dispatch the next order's hooks.
        // -----------------------------------------------------------------------
        public async Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (consumerId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerId));
            if (string.IsNullOrWhiteSpace(ackGuid)) throw new ArgumentNullException(nameof(ackGuid));
            var load = new DbExecutionLoad(ct);
            await AckManager.AckAsync(consumerId, ackGuid, outcome, message, retryAt, load);

            if (outcome == AckOutcome.Processed) {
                // Fetch hook context once — reused for all three checks below.
                // If this ack_guid belongs to a lifecycle transition (not a hook), GetContextByAckGuidAsync
                // returns null and we skip all three checks safely.
                DbRow? hookCtx = null;
                try {
                    hookCtx = await _dal.Hook.GetContextByAckGuidAsync(ackGuid, load);
                } catch (Exception ex) {
                    FireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                        $"Hook context lookup failed for ack={ackGuid}: {ex.Message}"));
                }

                // 1. AckMode=Any fan-out: mark all sibling ack_consumer rows Processed BEFORE group check
                //    so CountUnresolvedInGroup sees them as terminal.
                //    Scenario: hook has 3 consumers, ack_mode=Any. Consumer-A ACKs Processed →
                //    Consumer-B and Consumer-C rows are auto-marked Processed. Monitor won't retry them.
                if (hookCtx != null && hookCtx.GetInt("ack_mode") == 1) {
                    try {
                        await _dal.AckConsumer.MarkAllProcessedByAckIdAsync(hookCtx.GetLong("ack_id"), load);
                    } catch (Exception ex) {
                        FireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                            $"AckMode=Any fan-out failed for ack={ackGuid}: {ex.Message}"));
                    }
                }

                // 2. Group completion check
                try {
                    var ctx = await _dal.HookGroup.GetContextByAckGuidAsync(ackGuid, load);
                    if (ctx != null) {
                        var pending = await _dal.HookGroup.CountUnresolvedInGroupAsync(
                            ctx.GetLong("instance_id"), ctx.GetLong("state_id"), ctx.GetLong("via_event"),
                            ctx.GetBool("on_entry"), ctx.GetLong("group_id"), load);
                        if (pending == 0) {
                            var groupName = ctx.GetString("group_name") ?? string.Empty;
                            var instanceGuid = ctx.GetString("instance_guid") ?? string.Empty;
                            FireNotice(LifeCycleNotice.Info("HOOK_GROUP_COMPLETE", "HOOK_GROUP_COMPLETE",
                                $"All hooks in group '{groupName}' are processed. instance={instanceGuid}",
                                new Dictionary<string, object?> { ["groupName"] = groupName, ["instanceGuid"] = instanceGuid }));
                        }
                    }
                } catch (Exception ex) {
                    FireNotice(LifeCycleNotice.Warn("HOOK_GROUP_CHECK_ERROR", "HOOK_GROUP_CHECK_ERROR",
                        $"Group completion check failed for ack={ackGuid}: {ex.Message}"));
                }

                // 3. Order advancement (only when the ACKed hook is blocking)
                if (hookCtx != null && hookCtx.GetBool("blocking")) {
                    try {
                        var incomplete = await _dal.Hook.CountIncompleteBlockingInOrderAsync(
                            hookCtx.GetLong("instance_id"), hookCtx.GetLong("state_id"),
                            hookCtx.GetLong("via_event"), hookCtx.GetBool("on_entry"),
                            hookCtx.GetInt("order_seq"), load);
                        if (incomplete == 0)
                            await AdvanceNextHookOrderAsync(hookCtx, ct);
                    } catch (Exception ex) {
                        FireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                            $"Order advancement failed for ack={ackGuid}: {ex.Message}"));
                    }
                }
            }
        }

        public async Task<LifeCycleInstanceData?> GetInstanceDataAsync(LifeCycleInstanceKey key, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var row = await ResolveInstanceRowByKeyAsync(key, new DbExecutionLoad(ct));
            if (row == null) return null;
            return new LifeCycleInstanceData {
                InstanceId = row.GetLong("id"),
                InstanceGuid = row.GetString("guid") ?? string.Empty,
                DefinitionId = row.GetLong("def_id"),
                DefinitionVersionId = row.GetLong("def_version"),
                EntityId = row.GetString("entity_id") ?? string.Empty,
                CurrentStateId = row.GetLong("current_state"),
                Metadata = row.GetString("metadata"),
                Context = row.GetString("context")
            };
        }

        public async Task<string?> GetInstanceContextAsync(LifeCycleInstanceKey key, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var row = await ResolveInstanceRowByKeyAsync(key, new DbExecutionLoad(ct));
            return row?.GetString("context");
        }

        public async Task<int> SetInstanceContextAsync(LifeCycleInstanceKey key, string? context, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var load = new DbExecutionLoad(ct);
            var row = await ResolveInstanceRowByKeyAsync(key, load);
            if (row == null) return 0;
            var instanceId = row.GetLong("id");
            if (instanceId <= 0) return 0;
            return await _dal.Instance.SetContextAsync(instanceId, context, load);
        }

        public async Task<string?> GetTimelineJsonAsync(LifeCycleInstanceKey key, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var load = new DbExecutionLoad(ct);
            var row = await ResolveInstanceRowByKeyAsync(key, load);
            if (row == null) return null;
            return await _dal.LifeCycle.GetTimelineJsonByInstanceIdAsync(row.GetLong("id"), load);
        }

        public async Task<IReadOnlyList<InstanceRefItem>> GetInstanceRefsAsync(int envCode, string defName, LifeCycleInstanceFlag flags, int skip, int take, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var bp = await BlueprintManager.GetBlueprintLatestAsync(envCode, defName, ct);
            var rows = await _dal.Instance.ListByFlagsAndDefVersionPagedAsync(bp.DefVersionId, (uint)flags, skip, take);
            var result = new List<InstanceRefItem>(rows.Count);
            for (var i = 0; i < rows.Count; i++) {
                var r = rows[i];
                result.Add(new InstanceRefItem {
                    EntityId = r.GetString("entity_id") ?? string.Empty,
                    InstanceGuid = r.GetString("instance_guid") ?? string.Empty,
                    Created = r.GetDateTime("created")
                });
            }
            return result;
        }

        public Task ClearCacheAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Clear(); return Task.CompletedTask; }

        public Task InvalidateAsync(int envCode, string defName, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Invalidate(envCode, defName); return Task.CompletedTask; }

        public Task InvalidateAsync(long defVersionId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Invalidate(defVersionId); return Task.CompletedTask; }

        // Public entry point for a single monitor pass for ONE specific consumer.
        // Runs resend for both Pending rows (event never delivered) and Delivered rows (delivered but not ACKed).
        // Ignores consumers that are currently down (PushNextDueForDown handles postponing their rows).
        public async Task RunMonitorOnceAsync(long consumerId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            await ResendDispatchKindAsync(consumerId, (int)AckStatus.Pending, ct);
            await ResendDispatchKindAsync(consumerId, (int)AckStatus.Delivered, ct);
        }

        // Internal entry point called by the background Monitor timer on each tick.
        // Two responsibilities:
        //   1. Stale instance scan — fires STATE_STALE notices for instances stuck too long (no DB writes)
        //   2. Resend loop — for each active consumer returned by the Hub's ResolveConsumers callback,
        //      call RunMonitorOnceAsync to resend any overdue events
        internal async Task RunMonitorOnceInternalAsync(Func<LifeCycleConsumerType, CancellationToken, Task<IReadOnlyList<long>>> consumersProvider, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            await RaiseOverDueDefaultStateStaleNoticesAsync(ct);  // stale notice scan (no ACK, no DB writes)

            // consumersProvider is the Hub's ResolveConsumers callback with defVersionId=null (Monitor type).
            // Hub returns all currently active consumers so we resend for all of them.
            var consumerList = await consumersProvider(LifeCycleConsumerType.Monitor,ct);
            for (var i = 0; i < consumerList.Count; i++) {
                var consumerId = consumerList[i];
                await RunMonitorOnceAsync(consumerId, ct);
            }
        }


        // Registers a consumer process with the engine. Returns the engine-assigned numeric consumer ID.
        // Idempotent — safe to call every startup. The consumerGuid is the stable identity of the consumer
        // process (e.g. a fixed GUID in config). The numeric ID is what gets stored in ack_consumer rows.
        public Task<long> RegisterConsumerAsync(int envCode, string consumerGuid, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(consumerGuid)) throw new ArgumentNullException(nameof(consumerGuid));
            return BlueprintManager.EnsureConsumerIdAsync(envCode, consumerGuid, ct);
        }

        // Heartbeat — consumer calls this every N seconds to prove it is still alive.
        // The engine uses the last-beat timestamp to decide whether a consumer is "down" when the monitor
        // is deciding whether to postpone resending (no point retrying if the consumer is offline).
        public Task BeatConsumerAsync(int envCode, string consumerGuid, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(consumerGuid)) throw new ArgumentNullException(nameof(consumerGuid));
            return BlueprintManager.BeatConsumerAsync(envCode, consumerGuid, ct);
        }

        // Resolves the engine-assigned definition ID for a (envCode, definitionName) pair.
        // Used by the consumer library at startup to bind auto-discovered handler wrappers to their
        // internal def_id — so the consumer knows which definition it is subscribing to.
        public async Task<long?> GetDefinitionIdAsync(int envCode, string definitionName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(definitionName)) throw new ArgumentNullException(nameof(definitionName));
            var row = await _dal.Blueprint.GetLatestDefVersionByEnvCodeAndDefNameAsync(envCode, definitionName, new DbExecutionLoad(ct));
            var id = row?.GetLong("parent");
            return id > 0 ? id : null;
        }

        // Consumer-friendly overload: resolves the consumer by guid (envCode+guid → consumerId) then ACKs.
        // Useful for scenarios where the consumer only knows its guid, not its numeric ID.
        public async Task AckAsync(int envCode, string consumerGuid, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(consumerGuid)) throw new ArgumentNullException(nameof(consumerGuid));
            var consumerId = await BlueprintManager.EnsureConsumerIdAsync(envCode, consumerGuid, ct);
            await AckAsync(consumerId, ackGuid, outcome, message, retryAt, ct);
        }

        public async Task<long> UpsertRuntimeAsync(RuntimeLogByNameRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrWhiteSpace(req.Activity)) throw new ArgumentNullException(nameof(req.Activity));
            if (string.IsNullOrWhiteSpace(req.Status)) throw new ArgumentNullException(nameof(req.Status));
            if (string.IsNullOrWhiteSpace(req.ActorId)) throw new ArgumentNullException(nameof(req.ActorId));

            var load = new DbExecutionLoad(ct);
            var instanceRow = await ResolveInstanceRowByKeyAsync(req.Instance, load);
            if (instanceRow == null) throw new InvalidOperationException("Instance not found for the provided LifeCycleInstanceKey.");

            var activityId = await Runtime.EnsureActivityAsync(req.Activity, ct);
            var statusId   = await Runtime.EnsureActivityStatusAsync(req.Status, ct);

            return await Runtime.UpsertAsync(new RuntimeLogByIdRequest {
                InstanceGuid = instanceRow.GetString("guid") ?? string.Empty,
                ActivityId   = activityId,
                StateId      = req.StateId,
                ActorId      = req.ActorId,
                StatusId     = statusId,
                LcId         = req.LcId,
                Frozen       = req.Frozen,
                Data         = req.Data ?? new { },
                Payload      = req.Payload ?? new { }
            }, ct);
        }

        public async Task<int> SetRuntimeStatusAsync(LifeCycleRuntimeRef runtimeRef, string status, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var runtimeId = await ResolveRuntimeIdAsync(runtimeRef, ct);
            return await Runtime.SetStatusAsync(runtimeId, status, ct);
        }

        public async Task<int> FreezeRuntimeAsync(LifeCycleRuntimeRef runtimeRef, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var runtimeId = await ResolveRuntimeIdAsync(runtimeRef, ct);
            return await Runtime.SetFrozenAsync(runtimeId, true, ct);
        }

        public async Task<int> UnfreezeRuntimeAsync(LifeCycleRuntimeRef runtimeRef, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var runtimeId = await ResolveRuntimeIdAsync(runtimeRef, ct);
            return await Runtime.SetFrozenAsync(runtimeId, false, ct);
        }

        // Resolves a LifeCycleInstanceKey → instance DbRow.
        // Priority: InstanceGuid (direct) → EnvCode+DefName+EntityId (via blueprint lookup, fully environment-scoped).
        async Task<DbRow?> ResolveInstanceRowByKeyAsync(LifeCycleInstanceKey key, DbExecutionLoad load) {
            if (key == null) throw new ArgumentNullException(nameof(key));

            if (!string.IsNullOrWhiteSpace(key.InstanceGuid))
                return await _dal.Instance.GetByGuidAsync(key.InstanceGuid, load);

            if (string.IsNullOrWhiteSpace(key.DefName))  throw new ArgumentException("LifeCycleInstanceKey requires InstanceGuid OR (EnvCode + DefName + EntityId).", nameof(key));
            if (string.IsNullOrWhiteSpace(key.EntityId)) throw new ArgumentException("LifeCycleInstanceKey requires InstanceGuid OR (EnvCode + DefName + EntityId).", nameof(key));

            // Use envCode + defName — the only unambiguous combination.
            var defVersionRow = await _dal.Blueprint.GetLatestDefVersionByEnvCodeAndDefNameAsync(key.EnvCode, key.DefName, load);
            if (defVersionRow == null) return null;
            var defId = defVersionRow.GetLong("parent");
            if (defId <= 0) return null;

            return await _dal.Instance.GetByDefIdAndEntityIdAsync(defId, key.EntityId, load);
        }

        // Resolves a LifeCycleRuntimeRef → internal runtime row id.
        // Priority: Id (if caller already has it) → Instance+Activity+ActorId+StateId business-key lookup.
        async Task<long> ResolveRuntimeIdAsync(LifeCycleRuntimeRef runtimeRef, CancellationToken ct) {
            if (runtimeRef == null) throw new ArgumentNullException(nameof(runtimeRef));

            if (runtimeRef.Id.HasValue && runtimeRef.Id.Value > 0)
                return runtimeRef.Id.Value;

            var load = new DbExecutionLoad(ct);

            var instanceRow = await ResolveInstanceRowByKeyAsync(runtimeRef.Instance, load);
            if (instanceRow == null) throw new InvalidOperationException("Instance not found for the provided LifeCycleRuntimeRef.");
            var instanceId = instanceRow.GetLong("id");

            var activityRow = await _dal.Activity.GetByNameAsync(runtimeRef.Activity, load);
            if (activityRow == null) throw new InvalidOperationException($"Activity '{runtimeRef.Activity}' not found.");
            var activityId = activityRow.GetLong("id");

            var runtimeRow = await _dal.Runtime.GetByKeyAsync(instanceId, activityId, runtimeRef.StateId, runtimeRef.ActorId, load);
            if (runtimeRow == null) throw new InvalidOperationException($"Runtime entry not found for activity='{runtimeRef.Activity}' actor='{runtimeRef.ActorId}' state={runtimeRef.StateId}.");

            return runtimeRow.GetLong("id");
        }


        // Called when all blocking hooks at the current order_seq are Processed.
        // Finds the next undispatched order, atomically creates ACK rows + marks hooks dispatched,
        // then fires the events. Loops if the new order has no blocking hooks (non-blocking-only orders
        // need no ACK to advance further, so we immediately move to the next order in the same call).
        async Task AdvanceNextHookOrderAsync(DbRow hookCtx, CancellationToken ct) {
            var instanceId   = hookCtx.GetLong("instance_id");
            var stateId      = hookCtx.GetLong("state_id");
            var viaEvent     = hookCtx.GetLong("via_event");
            var onEntry      = hookCtx.GetBool("on_entry");
            var defVersionId = hookCtx.GetLong("def_version_id");
            var instanceGuid = hookCtx.GetString("instance_guid") ?? string.Empty;
            var entityId     = hookCtx.GetString("entity_id") ?? string.Empty;
            var definitionId = hookCtx.GetLong("definition_id");
            var metadata     = hookCtx.GetString("metadata");

            var hookConsumers = await AckManager.GetHookConsumersAsync(defVersionId, ct);
            var normConsumers = NormalizeConsumers(hookConsumers);

            var baseLcEvent = new LifeCycleEvent {
                InstanceGuid       = instanceGuid,
                DefinitionId       = definitionId,
                DefinitionVersionId = defVersionId,
                EntityId           = entityId,
                Metadata           = metadata,
                OccurredAt         = DateTimeOffset.UtcNow,
                AckRequired        = true
            };

            while (!ct.IsCancellationRequested) {
                var scanLoad = new DbExecutionLoad(ct);

                var nextOrderRaw = await _dal.Hook.GetMinUndispatchedOrderAsync(instanceId, stateId, viaEvent, onEntry, scanLoad);
                if (nextOrderRaw == null) return;
                var nextOrder = nextOrderRaw.Value;

                var nextHooks = await _dal.Hook.ListUndispatchedByOrderAsync(instanceId, stateId, viaEvent, onEntry, nextOrder, scanLoad);
                if (nextHooks.Count == 0) return;

                // Create ACK rows + mark hooks dispatched atomically in a transaction.
                // Fire events only after commit — same pattern as TriggerAsync.
                var toDispatch = new List<ILifeCycleEvent>(nextHooks.Count * (normConsumers.Count > 0 ? normConsumers.Count : 1));
                var transaction = _dal.CreateNewTransaction();
                using var tx = transaction.Begin(false);
                var txLoad = new DbExecutionLoad(ct, transaction);
                var committed = false;

                try {
                    for (var j = 0; j < nextHooks.Count; j++) {
                        var hookRow   = nextHooks[j];
                        var hookId    = hookRow.GetLong("id");
                        var isBlocking = hookRow.GetBool("blocking");
                        var ackMode   = hookRow.GetInt("ack_mode");
                        var route     = hookRow.GetString("route") ?? string.Empty;
                        var groupName = hookRow.GetString("group_name");
                        var hookOnEntry = hookRow.GetBool("on_entry");

                        var hookAck     = await AckManager.CreateHookAckAsync(hookId, normConsumers, (int)AckStatus.Pending, txLoad);
                        var hookAckGuid = hookAck.AckGuid ?? string.Empty;
                        await _dal.Hook.MarkDispatchedAsync(hookId, txLoad);

                        for (var i = 0; i < normConsumers.Count; i++) {
                            toDispatch.Add(new LifeCycleHookEvent(baseLcEvent) {
                                ConsumerId = normConsumers[i],
                                AckGuid    = hookAckGuid,
                                OnEntry    = hookOnEntry,
                                Route      = route,
                                IsBlocking = isBlocking,
                                GroupName  = groupName,
                                OrderSeq   = nextOrder,
                                AckMode    = ackMode,
                                AckRequired = true
                            });
                        }
                    }
                    transaction.Commit();
                    committed = true;
                } catch {
                    if (!committed) { try { transaction.Rollback(); } catch { } }
                    throw;
                }

                await DispatchEventsSafeAsync(toDispatch, ct);
                FireNotice(LifeCycleNotice.Info("HOOK_ORDER_ADVANCED", "HOOK_ORDER_ADVANCED",
                    $"Next-order hooks dispatched. order={nextOrder} instance={instanceGuid}",
                    new Dictionary<string, object?> { ["orderSeq"] = nextOrder, ["instanceGuid"] = instanceGuid }));

                // If no blocking hooks in this order no one will call AckAsync to trigger further
                // advancement, so loop immediately to dispatch the next order too.
                var anyBlocking = false;
                for (var j = 0; j < nextHooks.Count; j++) {
                    if (nextHooks[j].GetBool("blocking")) { anyBlocking = true; break; }
                }
                if (anyBlocking) break;  // wait for consumer ACKs before advancing further
            }
        }

        // Resends all overdue events of a given status (Pending or Delivered) for one consumer.
        //
        // "Pending" means the event was never delivered at all (e.g. process was down when TriggerAsync fired).
        // "Delivered" means the event was delivered but the consumer hasn't called AckAsync yet (still processing).
        //
        // Flow:
        //   1. If the consumer is currently DOWN (last heartbeat older than ConsumerTtlSeconds), push all its
        //      due rows into the future by ConsumerDownRecheckSeconds — no point re-firing to a dead process.
        //   2. List all rows that are now due for resend (next_due <= now).
        //   3. For each row: if retry count exceeded MaxRetryCount → mark Failed + suspend the instance.
        //      Otherwise: bump next_due (back-off), fire ACK_RETRY notice, re-fire the event.
        async Task ResendDispatchKindAsync(long consumerId, int ackStatus, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var load = new DbExecutionLoad(ct);
            var nowUtc = DateTime.UtcNow;
            var nextDueUtc = ackStatus == (int)AckStatus.Pending ? nowUtc.Add(_opt.AckPendingResendAfter) : nowUtc.Add(_opt.AckDeliveredResendAfter);
            var ttlSeconds = _opt.ConsumerTtlSeconds > 0 ? _opt.ConsumerTtlSeconds : 30;
            var recheckSeconds = _opt.ConsumerDownRecheckSeconds;

            // If consumer is down, push their overdue rows into the future so we don't spam when they come back up.
            await _dal.AckConsumer.PushNextDueForDownAsync(consumerId, ackStatus, ttlSeconds, recheckSeconds, load);

            // Lifecycle transition events — resend
            var lc = await AckManager.ListDueLifecycleDispatchAsync(consumerId, ackStatus, ttlSeconds, 0, _opt.MonitorPageSize, load);
            for (var i = 0; i < lc.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = lc[i];

                if (item.TriggerCount >= _opt.MaxRetryCount) {
                    await _dal.AckConsumer.SetStatusAndDueAsync(item.AckId, item.ConsumerId, (int)AckStatus.Failed, null, load);

                    var instanceId = await _dal.Instance.GetIdByGuidAsync(item.Event.InstanceGuid, load) ?? 0;
                    if (instanceId > 0) {
                        var msg = $"Suspended: ack max retries exceeded (max={_opt.MaxRetryCount}) kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}";
                        await _dal.Instance.SuspendWithMessageAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended, msg, load);
                        FireNotice(LifeCycleNotice.Warn("ACK_SUSPEND", "ACK_SUSPEND", msg));
                    } else {
                        FireNotice(LifeCycleNotice.Warn("ACK_FAIL", "ACK_FAIL", $"Ack marked failed (max retries) but instance not found. kind=lifecycle ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                    }

                    continue;
                }

                await _dal.AckConsumer.MarkTriggerAsync(item.AckId, item.ConsumerId, nextDueUtc, load);
                FireNotice(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                FireEvent(item.Event);
            }

            // Hook events — same resend logic as lifecycle events above, but blocking vs non-blocking
            // hooks are treated differently: a blocking hook failure suspends the instance; a non-blocking
            // hook failure just fires a notice and is abandoned (it doesn't block the workflow).
            var hk = await AckManager.ListDueHookDispatchAsync(consumerId, ackStatus, ttlSeconds, 0, _opt.MonitorPageSize, load);
            for (var i = 0; i < hk.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = hk[i];

                if (item.TriggerCount >= _opt.MaxRetryCount) {
                    await _dal.AckConsumer.SetStatusAndDueAsync(item.AckId, item.ConsumerId, (int)AckStatus.Failed, null, load);

                    var isBlocking = item.Event is ILifeCycleHookEvent hev && hev.IsBlocking;
                    if (isBlocking) {
                        var instanceId = await _dal.Instance.GetIdByGuidAsync(item.Event.InstanceGuid, load) ?? 0;
                        if (instanceId > 0) {
                            var msg = $"Suspended: ack max retries exceeded (max={_opt.MaxRetryCount}) kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}";
                            await _dal.Instance.SuspendWithMessageAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended, msg, load);
                            FireNotice(LifeCycleNotice.Warn("ACK_SUSPEND", "ACK_SUSPEND", msg));
                        } else {
                            FireNotice(LifeCycleNotice.Warn("ACK_FAIL", "ACK_FAIL", $"Ack marked failed (max retries) but instance not found. kind=hook ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                        }
                    } else {
                        var hookRoute = item.Event is ILifeCycleHookEvent h ? h.Route : string.Empty;
                        FireNotice(LifeCycleNotice.Warn("ACK_FAIL", "ACK_FAIL", $"Non-blocking hook failed after max retries. kind=hook ack={item.AckGuid} consumer={item.ConsumerId} route={hookRoute} instance={item.Event.InstanceGuid}"));
                    }

                    continue;
                }

                await _dal.AckConsumer.MarkTriggerAsync(item.AckId, item.ConsumerId, nextDueUtc, load);
                FireNotice(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                FireEvent(item.Event);
            }
        }

        // Iterates the event list and fires each one. Called after the DB transaction commits.
        async Task DispatchEventsSafeAsync(IReadOnlyList<ILifeCycleEvent> events, CancellationToken ct) {
            for (var i = 0; i < events.Count; i++) { ct.ThrowIfCancellationRequested(); FireEvent(events[i]); }
        }

        // Fires an event to all EventRaised subscribers. Each subscriber runs as an independent background
        // task (fire-and-forget). If a subscriber throws, it fires an EVENT_HANDLER_ERROR notice — it does
        // NOT propagate back to the caller. The monitor resends if the ACK row stays Pending.
        void FireEvent(ILifeCycleEvent e) {
            var h = EventRaised;
            if (h == null) return;
            foreach (Func<ILifeCycleEvent, Task> sub in h.GetInvocationList()) {
                _ = RunHandlerSafeAsync(() => sub(e));
            }
        }

        // Fires a notice to all NoticeRaised subscribers. Errors are swallowed (swallow=true) to prevent
        // an infinite loop where a broken notice handler causes another notice, which causes another...
        void FireNotice(LifeCycleNotice n) {
            var h = NoticeRaised;
            if (h == null) return;
            foreach (Func<LifeCycleNotice, Task> sub in h.GetInvocationList()) {
                _ = RunHandlerSafeAsync(() => sub(n), swallow: true);
            }
        }

        async Task RunHandlerSafeAsync(Func<Task> work, bool swallow = false) {
            try {
                await work().ConfigureAwait(false);
            } catch (Exception ex) {
                if (swallow) return;
                try {
                    FireNotice(LifeCycleNotice.Error("EVENT_HANDLER_ERROR", "EVENT_HANDLER_ERROR", ex.Message, ex));
                } catch { }
            }
        }

        // Deduplicates and filters the consumer list — removes zeros and duplicates.
        // The ResolveConsumers callback is external code; we can't trust it to return a clean list.
        static IReadOnlyList<long> NormalizeConsumers(IReadOnlyList<long>? consumers) {
            if (consumers == null || consumers.Count == 0) return new long[] {};
            var list = new List<long>(consumers.Count);
            for (var i = 0; i < consumers.Count; i++) { var c = consumers[i]; if (c > 0 && !list.Contains(c)) list.Add(c); }
            return list;
        }

        // Scans for instances that have been sitting in the same state longer than DefaultStateStaleDuration
        // without any resolved (Processed) lifecycle entry. This is purely a notification scan — no DB writes,
        // no state changes. It fires STATE_STALE notices so the host can alert or take corrective action.
        //
        // Why throttle in memory? The scan runs on every monitor tick. Without throttling, the same stale
        // instance would fire a notice every 2 minutes forever. The _overDueLastAt map ensures we only
        // fire once per (consumer, instance, state) combination per DefaultStateStaleDuration window.
        async Task RaiseOverDueDefaultStateStaleNoticesAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var dur = _opt.DefaultStateStaleDuration;
            if (dur <= TimeSpan.Zero) return; // disabled — host opted out by setting duration to zero

            var staleSeconds = (int)Math.Max(1, dur.TotalSeconds);
            var processed = (int)AckStatus.Processed;
            var excluded = (uint)(LifeCycleInstanceFlag.Suspended | LifeCycleInstanceFlag.Completed | LifeCycleInstanceFlag.Failed | LifeCycleInstanceFlag.Archived);

            var take = _opt.MonitorPageSize > 0 ? _opt.MonitorPageSize : 200;
            var skip = 0;
            var now = DateTimeOffset.UtcNow;

            var resolver = _opt.ResolveConsumers;
            var consumerCache = new Dictionary<long, IReadOnlyList<long>>();

            // crude safety cap (since throttle is in-memory)
            if (_overDueLastAt.Count > 200_000) _overDueLastAt.Clear();

            while (!ct.IsCancellationRequested) {
                var rows = await _dal.Instance.ListStaleByDefaultStateDurationPagedAsync(staleSeconds, processed, excluded, skip, take, new DbExecutionLoad(ct));
                if (rows.Count == 0) break;

                for (var i = 0; i < rows.Count; i++) {
                    ct.ThrowIfCancellationRequested();

                    var r = rows[i];
                    var instanceId = Convert.ToInt64(r["instance_id"]);
                    var instanceGuid = (string)r["instance_guid"];
                    var entityId = r["entity_id"] as string ?? string.Empty;
                    var defVersionId = Convert.ToInt64(r["def_version_id"]);
                    var stateId = Convert.ToInt64(r["current_state_id"]);
                    var stateName = r["state_name"] as string ?? string.Empty;
                    var lcId = Convert.ToInt64(r["lc_id"]);
                    var staleSec = Convert.ToInt64(r["stale_seconds"]);

                    IReadOnlyList<long> consumers;
                    if (resolver == null) {
                        consumers = Array.Empty<long>();
                    } else if (!consumerCache.TryGetValue(defVersionId, out consumers!)) {
                        consumers = NormalizeConsumers(await resolver(LifeCycleConsumerType.Transition, defVersionId, ct));
                        consumerCache[defVersionId] = consumers;
                    }

                    if (consumers.Count == 0) {
                        var k0 = $"0:{instanceId}:{stateId}";
                        if (_overDueLastAt.TryGetValue(k0, out var last0) && (now - last0) < dur) continue;
                        _overDueLastAt[k0] = now;

                        FireNotice(new LifeCycleNotice(LifeCycleNoticeKind.OverDue, "STATE_STALE", "DEFAULT_STATE_STALE", $"Instance stale (no consumers resolved). instance={instanceGuid} entity={entityId} state={stateName} stale={TimeSpan.FromSeconds(staleSec)}", null, new Dictionary<string, object?> {
                            ["consumerId"] = 0L,
                            ["instanceId"] = instanceId,
                            ["instanceGuid"] = instanceGuid,
                            ["entityId"] = entityId,
                            ["defVersionId"] = defVersionId,
                            ["currentStateId"] = stateId,
                            ["stateName"] = stateName,
                            ["lcId"] = lcId,
                            ["staleSeconds"] = staleSec
                        }));
                        continue;
                    }

                    for (var c = 0; c < consumers.Count; c++) {
                        var consumerId = consumers[c];
                        var key = $"{consumerId}:{instanceId}:{stateId}";
                        if (_overDueLastAt.TryGetValue(key, out var last) && (now - last) < dur) continue;
                        _overDueLastAt[key] = now;

                        FireNotice(new LifeCycleNotice(LifeCycleNoticeKind.OverDue, "STATE_STALE", "DEFAULT_STATE_STALE", $"Instance stale. consumer={consumerId} instance={instanceGuid} entity={entityId} state={stateName} stale={TimeSpan.FromSeconds(staleSec)}", null, new Dictionary<string, object?> {
                            ["consumerId"] = consumerId,
                            ["instanceId"] = instanceId,
                            ["instanceGuid"] = instanceGuid,
                            ["entityId"] = entityId,
                            ["defVersionId"] = defVersionId,
                            ["currentStateId"] = stateId,
                            ["stateName"] = stateName,
                            ["lcId"] = lcId,
                            ["staleSeconds"] = staleSec
                        }));
                    }
                }

                if (rows.Count < take) break;
                skip += take;
            }
        }

        // These delegate directly to BlueprintImporter — thin pass-through so the caller never needs to
        // know about internal sub-components. BlueprintImporter is idempotent (hash-guarded): re-importing
        // the same JSON is safe and a no-op if nothing changed. Call these every startup to stay in sync.
        public Task<long> ImportDefinitionJsonAsync(int envCode, string envDisplayName, string definitionJson, CancellationToken ct = default)=> BlueprintImporter.ImportDefinitionJsonAsync(envCode, envDisplayName, definitionJson, ct);

        public Task<long> ImportPolicyJsonAsync(int envCode, string envDisplayName, string policyJson, CancellationToken ct = default)=> BlueprintImporter.ImportPolicyJsonAsync(envCode , envDisplayName, policyJson, ct);

        // Ensures the environment row exists in the DB. Called by the consumer library at startup to
        // make sure envCode+displayName is registered before anything else runs.
        public Task<int> RegisterEnvironmentAsync(int envCode, string? envDisplayName, CancellationToken ct) => BlueprintManager.EnsureEnvironmentAsync(envCode, envDisplayName, new DbExecutionLoad(ct));
    }
}
