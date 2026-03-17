using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.KeyConstants;

namespace Haley.Services.Orchestrators {
    // Handles consumer ACK outcomes and follow-up orchestration that can be triggered by ACK=Processed.
    // This keeps WorkFlowEngine thin while preserving the same semantics:
    // - update ack status
    // - AckMode=Any sibling fan-out
    // - hook-group completion checks
    // - ordered blocking-hook advancement
    internal sealed class AckOutcomeOrchestrator {
        private readonly IWorkFlowDAL _dal;
        private readonly IAckManager _ackManager;
        private readonly Func<IReadOnlyList<ILifeCycleEvent>, CancellationToken, Task> _dispatchEventsAsync;
        private readonly Action<LifeCycleNotice> _fireNotice;

        // dispatchEventsAsync / fireNotice are engine callbacks so subscribers stay centralized.
        public AckOutcomeOrchestrator(IWorkFlowDAL dal, IAckManager ackManager, Func<IReadOnlyList<ILifeCycleEvent>, CancellationToken, Task> dispatchEventsAsync, Action<LifeCycleNotice> fireNotice) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _ackManager = ackManager ?? throw new ArgumentNullException(nameof(ackManager));
            _dispatchEventsAsync = dispatchEventsAsync ?? throw new ArgumentNullException(nameof(dispatchEventsAsync));
            _fireNotice = fireNotice ?? throw new ArgumentNullException(nameof(fireNotice));
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

            // Ordered progression is only relevant for blocking hooks.
            if (hookCtx != null && hookCtx.GetBool(KEY_BLOCKING)) {
                try {
                    var incomplete = await _dal.Hook.CountIncompleteBlockingInOrderAsync(
                        hookCtx.GetLong(KEY_INSTANCE_ID), hookCtx.GetLong(KEY_STATE_ID),
                        hookCtx.GetLong(KEY_VIA_EVENT), hookCtx.GetBool(KEY_ON_ENTRY),
                        hookCtx.GetLong(KEY_LC_ID), hookCtx.GetInt(KEY_ORDER_SEQ), load);
                    if (incomplete == 0) await AdvanceNextHookOrderAsync(hookCtx, ct);
                } catch (Exception ex) {
                    _fireNotice(LifeCycleNotice.Warn("HOOK_ORDER_ADVANCE_ERROR", "HOOK_ORDER_ADVANCE_ERROR",
                    $"Order advancement failed for ack={ackGuid}: {ex.Message}"));
                }
            }
        }

        // Dispatches next undispatched hook order once current blocking order is complete.
        // Loop continues across consecutive non-blocking-only orders in same invocation.
        private async Task AdvanceNextHookOrderAsync(DbRow hookCtx, CancellationToken ct) {
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

            while (!ct.IsCancellationRequested) {
                var scanLoad = new DbExecutionLoad(ct);

                var nextOrderRaw = await _dal.Hook.GetMinUndispatchedOrderAsync(instanceId, stateId, viaEvent, onEntry, lcId, scanLoad);
                if (nextOrderRaw == null) return;
                var nextOrder = nextOrderRaw.Value;

                var nextHooks = await _dal.Hook.ListUndispatchedByOrderAsync(instanceId, stateId, viaEvent, onEntry, lcId, nextOrder, scanLoad);
                if (nextHooks.Count == 0) return;

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
                        var isBlocking = hookRow.GetBool(KEY_BLOCKING);
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
                                IsBlocking = isBlocking,
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

                // If this order has blocking hooks, wait for future ACKs to continue progression.
                var anyBlocking = false;
                for (var j = 0; j < nextHooks.Count; j++) {
                    if (nextHooks[j].GetBool(KEY_BLOCKING)) { anyBlocking = true; break; }
                }
                if (anyBlocking) break;
            }
        }

    }
}
