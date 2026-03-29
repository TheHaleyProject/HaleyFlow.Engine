using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.KeyConstants;
using static Haley.Enums.HookType;

namespace Haley.Services.Orchestrators {
    // Handles consumer ACK outcomes and follow-up orchestration that can be triggered by ACK=Processed.
    // This keeps WorkFlowEngine thin while preserving the same semantics:
    // - update ack status
    // - AckMode=Any sibling fan-out
    // - hook-group completion checks
    // - ordered gate/effect hook advancement
    // - gate-success drain: skip remaining gates, drain effects in order, then dispatch TransitionMode event
    internal sealed class AckOutcomeOrchestrator {
        private readonly IWorkFlowDAL _dal;
        private readonly IAckManager _ackManager;
        private readonly IBlueprintManager _blueprintManager;
        private readonly IPolicyEnforcer _policyEnforcer;
        private readonly WorkFlowEngineOptions _opt;
        private readonly Func<IReadOnlyList<ILifeCycleEvent>, CancellationToken, Task> _dispatchEventsAsync;
        private readonly Action<LifeCycleNotice> _fireNotice;

        // dispatchEventsAsync / fireNotice are engine callbacks so subscribers stay centralized.
        public AckOutcomeOrchestrator(IWorkFlowDAL dal, IAckManager ackManager, IBlueprintManager blueprintManager, IPolicyEnforcer policyEnforcer, WorkFlowEngineOptions opt, Func<IReadOnlyList<ILifeCycleEvent>, CancellationToken, Task> dispatchEventsAsync, Action<LifeCycleNotice> fireNotice) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _ackManager = ackManager ?? throw new ArgumentNullException(nameof(ackManager));
            _blueprintManager = blueprintManager ?? throw new ArgumentNullException(nameof(blueprintManager));
            _policyEnforcer = policyEnforcer ?? throw new ArgumentNullException(nameof(policyEnforcer));
            _opt = opt ?? throw new ArgumentNullException(nameof(opt));
            _dispatchEventsAsync = dispatchEventsAsync ?? throw new ArgumentNullException(nameof(dispatchEventsAsync));
            _fireNotice = fireNotice ?? throw new ArgumentNullException(nameof(fireNotice));
        }

        // Called by the monitor when an effect hook has been pending longer than EffectTimeoutSeconds.
        // Marks the ack_consumer as Failed, fires EFFECT_HOOK_ABANDONED, and advances hook ordering.
        // Effect hooks are fire-and-forget within their window — if no ACK arrives in time, move on.
        public async Task AbandonEffectHookAsync(long ackId, long consumerId, string ackGuid, string instanceGuid, string hookRoute, CancellationToken ct = default) {
            var load = new DbExecutionLoad(ct);
            await _dal.AckConsumer.SetStatusAndDueAsync(ackId, consumerId, (int)AckStatus.Failed, null, null, load);
            _fireNotice(LifeCycleNotice.Warn("EFFECT_HOOK_ABANDONED", "EFFECT_HOOK_ABANDONED",
                $"Effect hook timed out and was abandoned. route={hookRoute} ack={ackGuid} consumer={consumerId} instance={instanceGuid}",
                new Dictionary<string, object?> { ["ackGuid"] = ackGuid, ["hookRoute"] = hookRoute, ["instanceGuid"] = instanceGuid }));

            // Fetch hook context to drive order advancement.
            var hookCtx = await _dal.Hook.GetContextByAckGuidAsync(ackGuid, load);
            if (hookCtx == null) return;

            try {
                await AdvanceAndCheckTransitionModeAsync(hookCtx, ct);
            } catch (Exception ex) {
                _fireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                    $"Order advancement failed after effect abandon. ack={ackGuid}: {ex.Message}"));
            }
        }

        // ACK flow:
        // 1. Persist ack outcome through AckManager.
        // 2. For non-Processed outcomes, stop.
        // 3. For Processed, run hook-related checks and ordered progression.
        public async Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (consumerId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerId));
            if (string.IsNullOrWhiteSpace(ackGuid)) throw new ArgumentNullException(nameof(ackGuid));
            var load = new DbExecutionLoad(ct);
            await _ackManager.AckAsync(consumerId, ackGuid, outcome, message, retryAt, load);

            // Retry/Dead do not participate in completion-based progression.
            if (outcome != AckOutcome.Processed) return;

            // Single hook context lookup reused by fan-out and order-advancement checks.
            // If ackGuid belongs to lifecycle transition ACK, hook context is null and checks are skipped.
            DbRow? hookCtx = null;
            try {
                hookCtx = await _dal.Hook.GetContextByAckGuidAsync(ackGuid, load);
            } catch (Exception ex) {
                _fireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                    $"Hook context lookup failed for ack={ackGuid}: {ex.Message}"));
            }

            // AckMode=Any: one consumer processing success completes the whole sibling set.
            if (hookCtx != null && hookCtx.GetInt(KEY_ACK_MODE) == 1) {
                try {
                    await _dal.AckConsumer.MarkAllProcessedByAckIdAsync(hookCtx.GetLong(KEY_ACK_ID), load);
                } catch (Exception ex) {
                    _fireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                        $"AckMode=Any fan-out failed for ack={ackGuid}: {ex.Message}"));
                }
            }

            // Group-level completion notice (observability only, no state mutation).
            try {
                var ctx = await _dal.HookGroup.GetContextByAckGuidAsync(ackGuid, load);
                if (ctx != null) {
                    var pending = await _dal.HookGroup.CountUnresolvedInGroupAsync(
                        ctx.GetLong(KEY_INSTANCE_ID), ctx.GetLong(KEY_STATE_ID), ctx.GetLong(KEY_VIA_EVENT),
                        ctx.GetBool(KEY_ON_ENTRY), ctx.GetLong(KEY_LC_ID), ctx.GetLong(KEY_GROUP_ID), load);
                    if (pending == 0) {
                        var groupName = ctx.GetString(KEY_GROUP_NAME) ?? string.Empty;
                        var instanceGuid = ctx.GetString(KEY_INSTANCE_GUID) ?? string.Empty;
                        _fireNotice(LifeCycleNotice.Info("HOOK_GROUP_COMPLETE", "HOOK_GROUP_COMPLETE",
                            $"All hooks in group '{groupName}' are processed. instance={instanceGuid}",
                            new Dictionary<string, object?> { ["groupName"] = groupName, ["instanceGuid"] = instanceGuid }));
                    }
                }
            } catch (Exception ex) {
                _fireNotice(LifeCycleNotice.Warn("HOOK_GROUP_CHECK_ERROR", "HOOK_GROUP_CHECK_ERROR",
                    $"Group completion check failed for ack={ackGuid}: {ex.Message}"));
            }

            if (hookCtx == null) return;

            var hookType = (HookType)hookCtx.GetInt(KEY_HOOK_TYPE);

            // Gate hooks: check if the current order is fully resolved before advancing.
            if (hookType == HookType.Gate) {
                try {
                    var incomplete = await _dal.Hook.CountIncompleteBlockingInOrderAsync(
                        hookCtx.GetLong(KEY_INSTANCE_ID), hookCtx.GetLong(KEY_STATE_ID),
                        hookCtx.GetLong(KEY_VIA_EVENT), hookCtx.GetBool(KEY_ON_ENTRY),
                        hookCtx.GetLong(KEY_LC_ID), hookCtx.GetInt(KEY_ORDER_SEQ), load);
                    if (incomplete == 0) {
                        // Check if this gate has an OnSuccessEvent — if so, skip all remaining undispatched gates
                        // before draining effects. This implements the gate-success execution contract.
                        await TrySkipRemainingGatesIfSuccessCodeAsync(hookCtx, ct);
                        await AdvanceAndCheckTransitionModeAsync(hookCtx, ct);
                    }
                } catch (Exception ex) {
                    _fireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                        $"Order advancement failed for ack={ackGuid}: {ex.Message}"));
                }
                return;
            }

            // Effect hooks drive progression only when all gate hooks at the same order are resolved.
            // If a gate shares order_seq with this effect, it must ACK first — the gate path owns advancement.
            if (hookType == HookType.Effect) {
                try {
                    var incompleteGates = await _dal.Hook.CountIncompleteBlockingInOrderAsync(
                        hookCtx.GetLong(KEY_INSTANCE_ID), hookCtx.GetLong(KEY_STATE_ID),
                        hookCtx.GetLong(KEY_VIA_EVENT), hookCtx.GetBool(KEY_ON_ENTRY),
                        hookCtx.GetLong(KEY_LC_ID), hookCtx.GetInt(KEY_ORDER_SEQ), load);
                    if (incompleteGates == 0)
                        await AdvanceAndCheckTransitionModeAsync(hookCtx, ct);
                } catch (Exception ex) {
                    _fireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                        $"Effect order advancement failed for ack={ackGuid}: {ex.Message}"));
                }
            }
        }

        // If the current gate hook has an OnSuccessEvent in policy, skip all remaining undispatched gates
        // so only effect hooks remain to be drained. Idempotent — gates already dispatched are unaffected.
        private async Task TrySkipRemainingGatesIfSuccessCodeAsync(DbRow hookCtx, CancellationToken ct) {
            var defVersionId = hookCtx.GetLong(KEY_DEF_VERSION_ID);
            var policyId = hookCtx.GetLong(KEY_POLICY_ID);
            var stateId = hookCtx.GetLong(KEY_STATE_ID);
            var viaEventId = hookCtx.GetLong(KEY_VIA_EVENT);
            var route = hookCtx.GetString(KEY_ROUTE) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(route)) return;

            var policy = await _policyEnforcer.ResolvePolicyByIdAsync(policyId, new DbExecutionLoad(ct));
            if (string.IsNullOrWhiteSpace(policy.PolicyJson)) return;

            var bp = await _blueprintManager.GetBlueprintByVersionIdAsync(defVersionId, ct);
            bp.StatesById.TryGetValue(stateId, out var toState);
            bp.EventsById.TryGetValue(viaEventId, out var viaEvent);
            if (toState == null) return;

            var hctx = _policyEnforcer.ResolveHookContextFromJson(policy.PolicyJson!, toState, viaEvent, route, ct, policyId);
            if (string.IsNullOrWhiteSpace(hctx.OnSuccessEvent)) return;

            // This gate has a success code — skip all remaining undispatched gates in scope.
            await _dal.Hook.SkipUndispatchedGateHooksAsync(
                hookCtx.GetLong(KEY_INSTANCE_ID), stateId, viaEventId,
                hookCtx.GetBool(KEY_ON_ENTRY), hookCtx.GetLong(KEY_LC_ID), new DbExecutionLoad(ct));
        }

        // Advances to the next undispatched hook order.
        // Returns true when all hooks in scope are dispatched (no more undispatched orders remain).
        // After all hooks dispatched, checks for a skipped gate — if found, dispatches TransitionMode event.
        private async Task AdvanceAndCheckTransitionModeAsync(DbRow hookCtx, CancellationToken ct) {
            var allDone = await AdvanceNextHookOrderAsync(hookCtx, ct);
            if (!allDone) return;

            // All hooks dispatched — check for a gate-success drain scenario.
            // If any gate was skipped (status=2), it means a gate succeeded with OnSuccessEvent.
            // After all effects drained, we now dispatch a TransitionMode event so the consumer
            // fires the next event code without running business logic again.
            var instanceId = hookCtx.GetLong(KEY_INSTANCE_ID);
            var lcId = hookCtx.GetLong(KEY_LC_ID);
            var instanceGuid = hookCtx.GetString(KEY_INSTANCE_GUID) ?? string.Empty;

            try {
                var skippedGate = await _dal.Hook.GetFirstSkippedGateRouteAsync(instanceId, lcId, new DbExecutionLoad(ct));
                if (skippedGate == null) return;

                // Re-resolve the gate's OnSuccessEvent from policy.
                var defVersionId = hookCtx.GetLong(KEY_DEF_VERSION_ID);
                var policyId = hookCtx.GetLong(KEY_POLICY_ID);
                var stateId = skippedGate.GetLong(KEY_STATE_ID);
                var viaEventId = skippedGate.GetLong(KEY_VIA_EVENT);
                var route = skippedGate.GetString(KEY_ROUTE) ?? string.Empty;

                var bp = await _blueprintManager.GetBlueprintByVersionIdAsync(defVersionId, ct);
                var policy = await _policyEnforcer.ResolvePolicyByIdAsync(policyId, new DbExecutionLoad(ct));
                if (string.IsNullOrWhiteSpace(policy.PolicyJson)) return;

                bp.StatesById.TryGetValue(stateId, out var toState);
                bp.EventsById.TryGetValue(viaEventId, out var viaEvent);
                if (toState == null) return;

                var hctx = _policyEnforcer.ResolveHookContextFromJson(policy.PolicyJson!, toState, viaEvent, route, ct, policyId);
                if (string.IsNullOrWhiteSpace(hctx.OnSuccessEvent)) return;

                await DispatchTransitionModeEventAsync(hookCtx, hctx.OnSuccessEvent!, ct);
            } catch (Exception ex) {
                _fireNotice(LifeCycleNotice.Warn("TRANSITION_MODE_DISPATCH_ERROR", "TRANSITION_MODE_DISPATCH_ERROR",
                    $"TransitionMode dispatch failed after effect drain. instance={instanceGuid}: {ex.Message}"));
            }
        }

        // Dispatches a new TransitionMode lifecycle transition event to all transition consumers.
        // This drives the consumer to fire the next event code without re-executing business logic.
        private async Task DispatchTransitionModeEventAsync(DbRow hookCtx, string onSuccessEvent, CancellationToken ct) {
            var defVersionId = hookCtx.GetLong(KEY_DEF_VERSION_ID);
            var instanceGuid = hookCtx.GetString(KEY_INSTANCE_GUID) ?? string.Empty;
            var entityId = hookCtx.GetString(KEY_ENTITY_ID) ?? string.Empty;
            var definitionId = hookCtx.GetLong(KEY_DEFINITION_ID);
            var metadata = hookCtx.GetString(KEY_METADATA);
            var lcId = hookCtx.GetLong(KEY_LC_ID);

            var consumers = await _ackManager.GetTransitionConsumersAsync(defVersionId, ct);
            var normConsumers = InternalUtils.NormalizeConsumers(consumers);
            if (normConsumers.Count == 0) return;

            var txLoad = new DbExecutionLoad(ct);
            var ackRef = await _ackManager.CreateLifecycleAckAsync(lcId, normConsumers, (int)AckStatus.Pending, txLoad);

            var toDispatch = new List<ILifeCycleEvent>(normConsumers.Count);
            for (var i = 0; i < normConsumers.Count; i++) {
                toDispatch.Add(new LifeCycleTransitionEvent {
                    ConsumerId = normConsumers[i],
                    InstanceGuid = instanceGuid,
                    DefinitionId = definitionId,
                    DefinitionVersionId = defVersionId,
                    EntityId = entityId,
                    Metadata = metadata,
                    OccurredAt = DateTimeOffset.UtcNow,
                    AckGuid = ackRef.AckGuid ?? string.Empty,
                    DispatchMode = TransitionDispatchMode.TransitionMode,
                    OnSuccessEvent = onSuccessEvent,
                    LifeCycleId = lcId,
                    EventCode = 0,
                    EventName = string.Empty
                });
            }

            await _dispatchEventsAsync(toDispatch, ct);
            _fireNotice(LifeCycleNotice.Info("TRANSITION_MODE_DISPATCHED", "TRANSITION_MODE_DISPATCHED",
                $"TransitionMode event dispatched after gate-success effect drain. instance={instanceGuid} onSuccessEvent={onSuccessEvent}",
                new Dictionary<string, object?> { ["instanceGuid"] = instanceGuid, ["onSuccessEvent"] = onSuccessEvent }));
        }

        // Dispatches the next undispatched hook order for the given lifecycle scope.
        // Returns true when no more undispatched orders remain (all done), false if a batch was dispatched
        // (meaning we should wait for those ACKs before advancing further).
        // Always breaks after dispatching one batch — both gate and effect batches wait for ACKs.
        private async Task<bool> AdvanceNextHookOrderAsync(DbRow hookCtx, CancellationToken ct) {
            var instanceId = hookCtx.GetLong(KEY_INSTANCE_ID);
            var stateId = hookCtx.GetLong(KEY_STATE_ID);
            var viaEvent = hookCtx.GetLong(KEY_VIA_EVENT);
            var onEntry = hookCtx.GetBool(KEY_ON_ENTRY);
            var lcId = hookCtx.GetLong(KEY_LC_ID);
            var defVersionId = hookCtx.GetLong(KEY_DEF_VERSION_ID);
            var instanceGuid = hookCtx.GetString(KEY_INSTANCE_GUID) ?? string.Empty;
            var entityId = hookCtx.GetString(KEY_ENTITY_ID) ?? string.Empty;
            var definitionId = hookCtx.GetLong(KEY_DEFINITION_ID);
            var metadata = hookCtx.GetString(KEY_METADATA);

            var hookConsumers = await _ackManager.GetHookConsumersAsync(defVersionId, ct);
            var normConsumers = InternalUtils.NormalizeConsumers(hookConsumers);

            var baseLcEvent = new LifeCycleEvent {
                InstanceGuid = instanceGuid,
                DefinitionId = definitionId,
                DefinitionVersionId = defVersionId,
                EntityId = entityId,
                Metadata = metadata,
                OccurredAt = DateTimeOffset.UtcNow
            };

            var scanLoad = new DbExecutionLoad(ct);
            var nextOrderRaw = await _dal.Hook.GetMinUndispatchedOrderAsync(instanceId, stateId, viaEvent, onEntry, lcId, scanLoad);
            if (nextOrderRaw == null) return true;  // all done
            var nextOrder = nextOrderRaw.Value;

            var nextHooks = await _dal.Hook.ListUndispatchedByOrderAsync(instanceId, stateId, viaEvent, onEntry, lcId, nextOrder, scanLoad);
            if (nextHooks.Count == 0) return true;  // all done

            // Keep hook_lc dispatched marking and ack rows in a single atomic unit.
            var toDispatch = new List<ILifeCycleEvent>(nextHooks.Count * (normConsumers.Count > 0 ? normConsumers.Count : 1));
            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var txLoad = new DbExecutionLoad(ct, transaction);
            var committed = false;

            try {
                for (var j = 0; j < nextHooks.Count; j++) {
                    var hookRow = nextHooks[j];
                    var hookId = hookRow.GetLong(KEY_ID);
                    var hookLcId = hookRow.GetLong(KEY_HOOK_LC_ID);
                    var hookType = (HookType)hookRow.GetInt(KEY_HOOK_TYPE);
                    var ackMode = hookRow.GetInt(KEY_ACK_MODE);
                    var route = hookRow.GetString(KEY_ROUTE) ?? string.Empty;
                    var groupName = hookRow.GetString(KEY_GROUP_NAME);
                    var hookOnEntry = hookRow.GetBool(KEY_ON_ENTRY);

                    var hookAck = await _ackManager.CreateHookAckAsync(hookLcId, normConsumers, (int)AckStatus.Pending, txLoad);
                    var hookAckGuid = hookAck.AckGuid ?? string.Empty;
                    await _dal.HookLc.MarkDispatchedAsync(hookLcId, txLoad);

                    var runCount = await _dal.HookLc.CountDispatchedByHookIdAsync(hookId, txLoad);
                    for (var i = 0; i < normConsumers.Count; i++) {
                        toDispatch.Add(new LifeCycleHookEvent(baseLcEvent) {
                            ConsumerId = normConsumers[i],
                            AckGuid = hookAckGuid,
                            OnEntry = hookOnEntry,
                            Route = route,
                            HookType = hookType,
                            GroupName = groupName,
                            OrderSeq = nextOrder,
                            AckMode = ackMode,
                            RunCount = runCount
                        });
                    }
                }
                transaction.Commit();
                committed = true;
            } catch {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                throw;
            }

            // Fire after commit so monitor can recover from downstream handler failure.
            await _dispatchEventsAsync(toDispatch, ct);
            _fireNotice(LifeCycleNotice.Info("HOOK_ORDER_ADVANCED", "HOOK_ORDER_ADVANCED",
                $"Next-order hooks dispatched. order={nextOrder} instance={instanceGuid}",
                new Dictionary<string, object?> { ["orderSeq"] = nextOrder, ["instanceGuid"] = instanceGuid }));

            // Always break after dispatching — wait for ACKs before advancing further.
            return false;
        }
    }
}
