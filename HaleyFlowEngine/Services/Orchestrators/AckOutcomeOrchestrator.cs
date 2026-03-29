using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.KeyConstants;
using static Haley.Enums.HookType;

namespace Haley.Services.Orchestrators {
    // Handles consumer ACK outcomes and the follow-up orchestration triggered by ACK=Processed.
    // This keeps WorkFlowEngine thin while preserving the same semantics:
    // - update ack status
    // - ValidationMode lifecycle ACK releases hook order 1
    // - AckMode=Any sibling fan-out
    // - hook-group completion checks
    // - ordered gate/effect hook advancement
    // - gate-success drain: skip remaining gates, drain effects in order, then dispatch Complete
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
                await AdvanceAndCheckCompletionAsync(hookCtx, ct);
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
            // If ackGuid belongs to a lifecycle transition ACK, hook context is null and we use the
            // lifecycle ACK path below to release the first hook batch for ValidationMode.
            DbRow? hookCtx = null;
            try {
                hookCtx = await _dal.Hook.GetContextByAckGuidAsync(ackGuid, load);
            } catch (Exception ex) {
                _fireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                    $"Hook context lookup failed for ack={ackGuid}: {ex.Message}"));
            }

            if (hookCtx == null) {
                try {
                    var lcId = await _dal.LcAck.GetLcIdByAckGuidAsync(ackGuid, load);
                    if (lcId.HasValue && lcId.Value > 0) {
                        var undispatchedHooks = await _dal.HookLc.CountUndispatchedByLcIdAsync(lcId.Value, load);
                        if (undispatchedHooks > 0) {
                            var hookCtxForLc = await _dal.Hook.GetContextByLcIdAsync(lcId.Value, load);
                            if (hookCtxForLc != null) {
                                await AdvanceNextHookOrderAsync(hookCtxForLc, ct);
                            }
                        }
                    }
                } catch (Exception ex) {
                    _fireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                        $"ValidationMode lifecycle ACK handling failed for ack={ackGuid}: {ex.Message}"));
                }
                return;
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
                        await AdvanceAndCheckCompletionAsync(hookCtx, ct);
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
                        await AdvanceAndCheckCompletionAsync(hookCtx, ct);
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
            var lcId = hookCtx.GetLong(KEY_LC_ID);
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
            if (!int.TryParse(hctx.OnSuccessEvent, out var resolvedNextEvent) || resolvedNextEvent <= 0) return;

            // This gate has a success code — skip all remaining undispatched gates in scope.
            await _dal.Hook.SkipUndispatchedGateHooksAsync(
                hookCtx.GetLong(KEY_INSTANCE_ID), stateId, viaEventId,
                hookCtx.GetBool(KEY_ON_ENTRY), hookCtx.GetLong(KEY_LC_ID), new DbExecutionLoad(ct));

            // Persist the winning next code now so later effect ACKs keep the same completion result.
            await _dal.LcNext.InsertAsync(lcId, resolvedNextEvent, hookCtx.GetLong(KEY_ACK_ID), new DbExecutionLoad(ct));
        }

        // Advances to the next undispatched hook order.
        // Returns true when all hooks in scope are dispatched (no more undispatched orders remain).
        // After all hooks dispatches/resolve, emit a Complete event carrying the engine-resolved next code.
        private async Task AdvanceAndCheckCompletionAsync(DbRow hookCtx, CancellationToken ct) {
            var allDone = await AdvanceNextHookOrderAsync(hookCtx, ct);
            if (!allDone) return;

            var instanceGuid = hookCtx.GetString(KEY_INSTANCE_GUID) ?? string.Empty;
            try {
                var resolvedNextEvent = await ResolveCompletionNextEventAsync(hookCtx, ct);
                await DispatchCompleteEventAsync(hookCtx, resolvedNextEvent, ct);
            } catch (Exception ex) {
                _fireNotice(LifeCycleNotice.Warn("COMPLETE_DISPATCH_ERROR", "COMPLETE_DISPATCH_ERROR",
                    $"Complete dispatch failed after hook resolution. instance={instanceGuid}: {ex.Message}"));
            }
        }

        private async Task<int> ResolveCompletionNextEventAsync(DbRow hookCtx, CancellationToken ct) {
            var lcId = hookCtx.GetLong(KEY_LC_ID);
            var defVersionId = hookCtx.GetLong(KEY_DEF_VERSION_ID);
            var policyId = hookCtx.GetLong(KEY_POLICY_ID);

            // If a terminal gate already resolved a winning next code, preserve it through effect drain.
            var storedNextEvent = await _dal.LcNext.GetNextEventByLcIdAsync(lcId, new DbExecutionLoad(ct));
            if (storedNextEvent.HasValue && storedNextEvent.Value > 0) return storedNextEvent.Value;

            // Normal all-hooks-done flow falls back to the transition rule's complete.success.
            var bp = await _blueprintManager.GetBlueprintByVersionIdAsync(defVersionId, ct);
            var policy = await _policyEnforcer.ResolvePolicyByIdAsync(policyId, new DbExecutionLoad(ct));
            if (string.IsNullOrWhiteSpace(policy.PolicyJson)) return 0;

            bp.StatesById.TryGetValue(hookCtx.GetLong(KEY_STATE_ID), out var toState);
            bp.EventsById.TryGetValue(hookCtx.GetLong(KEY_VIA_EVENT), out var viaEvent);
            if (toState == null) return 0;

            var ruleCtx = _policyEnforcer.ResolveRuleContextFromJson(policy.PolicyJson!, toState, viaEvent, ct, policyId);
            return int.TryParse(ruleCtx.OnSuccessEvent, out var nextEvent) && nextEvent > 0
                ? nextEvent
                : 0;
        }

        // Dispatches a Complete lifecycle event to all transition consumers.
        // The event carries the engine-resolved next event suggestion. Consumer code may accept it or override it.
        private async Task DispatchCompleteEventAsync(DbRow hookCtx, int resolvedNextEvent, CancellationToken ct) {
            var defVersionId = hookCtx.GetLong(KEY_DEF_VERSION_ID);
            var instanceGuid = hookCtx.GetString(KEY_INSTANCE_GUID) ?? string.Empty;
            var entityId = hookCtx.GetString(KEY_ENTITY_ID) ?? string.Empty;
            var definitionId = hookCtx.GetLong(KEY_DEFINITION_ID);
            var metadata = hookCtx.GetString(KEY_METADATA);
            var lcId = hookCtx.GetLong(KEY_LC_ID);

            var existingAckId = await _dal.LcNext.GetDispatchedAckIdByLcIdAsync(lcId, new DbExecutionLoad(ct));
            if (existingAckId.HasValue && existingAckId.Value > 0) return;

            var consumers = await _ackManager.GetTransitionConsumersAsync(defVersionId, ct);
            var normConsumers = InternalUtils.NormalizeConsumers(consumers);
            if (normConsumers.Count == 0)
                throw new InvalidOperationException($"No transition consumers found for complete dispatch. defVersionId={defVersionId} lcId={lcId}");

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var txLoad = new DbExecutionLoad(ct, transaction);
            var committed = false;
            IWorkFlowAckRef ackRef;
            try {
                await _dal.LcNext.InsertAsync(lcId, resolvedNextEvent, hookCtx.GetLong(KEY_ACK_ID), txLoad);
                ackRef = await _ackManager.CreateCompleteAckAsync(lcId, normConsumers, (int)AckStatus.Pending, txLoad);
                transaction.Commit();
                committed = true;
            } catch {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                throw;
            }

            var toDispatch = new List<ILifeCycleEvent>(normConsumers.Count);
            for (var i = 0; i < normConsumers.Count; i++) {
                toDispatch.Add(new LifeCycleCompleteEvent {
                    ConsumerId = normConsumers[i],
                    InstanceGuid = instanceGuid,
                    DefinitionId = definitionId,
                    DefinitionVersionId = defVersionId,
                    EntityId = entityId,
                    Metadata = metadata,
                    OccurredAt = DateTimeOffset.UtcNow,
                    AckGuid = ackRef.AckGuid ?? string.Empty,
                    LifeCycleId = lcId,
                    HooksSucceeded = true,
                    NextEvent = resolvedNextEvent
                });
            }

            await _dispatchEventsAsync(toDispatch, ct);
            _fireNotice(LifeCycleNotice.Info("COMPLETE_DISPATCHED", "COMPLETE_DISPATCHED",
                $"Complete event dispatched after hook resolution. instance={instanceGuid} nextEvent={resolvedNextEvent}",
                new Dictionary<string, object?> { ["instanceGuid"] = instanceGuid, ["nextEvent"] = resolvedNextEvent }));
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
            if (normConsumers.Count == 0)
                throw new InvalidOperationException($"No Hook consumers found for ordered hook dispatch. defVersionId={defVersionId} lcId={lcId}");

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
