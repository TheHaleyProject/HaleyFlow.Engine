using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.KeyConstants;

namespace Haley.Services.Orchestrators {
    // Owns instance-scoped lifecycle operations that were previously split across
    // separate trigger and reopen orchestrators.
    // Responsibilities:
    // - TriggerAsync: state transition, ACK creation, hook emission, and post-commit dispatch
    // - ReopenAsync: terminal-instance reset and optional auto-start bootstrap trigger
    // It does not own low-level persistence rules; those stay inside DAL/StateMachine/Policy services.
    internal sealed class InstanceOrchestrator {
        private readonly IWorkFlowDAL _dal;
        private readonly WorkFlowEngineOptions _opt;
        private readonly IBlueprintManager _blueprintManager;
        private readonly IStateMachine _stateMachine;
        private readonly IPolicyEnforcer _policyEnforcer;
        private readonly IAckManager _ackManager;
        private readonly Func<IReadOnlyList<ILifeCycleEvent>, CancellationToken, Task> _dispatchEventsAsync;
        private readonly Action<LifeCycleNotice> _fireNotice;

        // Dependencies are passed in from WorkFlowEngine so behavior stays identical after extraction.
        // dispatchEventsAsync and fireNotice are callbacks to engine-level event/notice pipelines.
        public InstanceOrchestrator(IWorkFlowDAL dal, WorkFlowEngineOptions opt, IBlueprintManager blueprintManager, IStateMachine stateMachine, IPolicyEnforcer policyEnforcer, IAckManager ackManager, Func<IReadOnlyList<ILifeCycleEvent>, CancellationToken, Task> dispatchEventsAsync, Action<LifeCycleNotice> fireNotice) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _opt = opt ?? throw new ArgumentNullException(nameof(opt));
            _blueprintManager = blueprintManager ?? throw new ArgumentNullException(nameof(blueprintManager));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _policyEnforcer = policyEnforcer ?? throw new ArgumentNullException(nameof(policyEnforcer));
            _ackManager = ackManager ?? throw new ArgumentNullException(nameof(ackManager));
            _dispatchEventsAsync = dispatchEventsAsync ?? throw new ArgumentNullException(nameof(dispatchEventsAsync));
            _fireNotice = fireNotice ?? throw new ArgumentNullException(nameof(fireNotice));
        }

        // TRIGGER flow (single logical operation):
        // 1. Validate request and resolve latest blueprint.
        // 2. Resolve transition consumers up front (must have at least one).
        // 3. Begin DB transaction.
        // 4. Ensure instance + apply transition + create ACK rows + emit hooks.
        // 5. Commit.
        // 6. Dispatch events only after commit.
        public async Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            // API boundary validation: fail fast with clear argument errors.
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrWhiteSpace(req.DefName)) throw new ArgumentNullException(nameof(req.DefName));
            if (string.IsNullOrWhiteSpace(req.EntityId)) throw new ArgumentNullException(nameof(req.EntityId));
            if (string.IsNullOrWhiteSpace(req.Event)) throw new ArgumentNullException(nameof(req.Event));

            // Start from latest blueprint; may be replaced below if instance is pinned to older version.
            var bp = await _blueprintManager.GetBlueprintLatestAsync(req.EnvCode, req.DefName, ct);

            // Transition events are mandatory fan-out events. If nobody can receive them we stop early.
            var transitionConsumers = await _ackManager.GetTransitionConsumersAsync(bp.DefVersionId, ct);
            if (transitionConsumers.Count < 1) throw new ArgumentException("No transition consumers found for this definition version. At least one transition consumer is required to proceed.", nameof(req));

            // All state change + ack/hook writes are atomic.
            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;

            // Collect outbound events and ack ids for response.
            var toDispatch = new List<ILifeCycleEvent>(8);
            var lcAckGuids = new List<string>(4);
            var hookAckGuids = new List<string>(8);

            DbRow instance = null!;
            ApplyTransitionResult transition = null!;
            PolicyResolution pr = null!;

            try {
                // Resolve latest policy first; new instance creation uses this policy id.
                var policy = await _policyEnforcer.ResolvePolicyAsync(bp.DefinitionId, load);
                instance = await _stateMachine.EnsureInstanceAsync(bp.DefVersionId, req.EntityId, policy.PolicyId ?? 0, req.Metadata, load);

                // Existing instances are version-locked. Reload exact blueprint if needed.
                var instanceDefVersion = instance.GetLong(KEY_DEF_VERSION);
                if (instanceDefVersion != bp.DefVersionId) {
                    bp = await _blueprintManager.GetBlueprintByVersionIdAsync(instanceDefVersion, ct);
                }

                // Guard rail: transitions are allowed only for active, non-suspended, non-terminal instances.
                var instanceFlags = (uint)instance.GetLong(KEY_FLAGS);
                var hasTerminal = (instanceFlags & (uint)(LifeCycleInstanceFlag.Completed | LifeCycleInstanceFlag.Failed | LifeCycleInstanceFlag.Archived)) != 0;
                var isSuspended = (instanceFlags & (uint)LifeCycleInstanceFlag.Suspended) != 0;
                var isActive = (instanceFlags & (uint)LifeCycleInstanceFlag.Active) != 0;
                if (hasTerminal || isSuspended || !isActive) {
                    transaction.Commit();
                    committed = true;
                    return new LifeCycleTriggerResult {
                        Applied = false,
                        InstanceGuid = instance.GetString(KEY_GUID) ?? string.Empty,
                        InstanceId = instance.GetLong(KEY_ID),
                        Reason = hasTerminal ? "InstanceIsTerminal" : "InstanceNotActive",
                        LifecycleAckGuids = Array.Empty<string>(),
                        HookAckGuids = Array.Empty<string>()
                    };
                }

                // Optional ACK gate: block new transition while prior lifecycle ACKs are unresolved.
                if (_opt.AckGateEnabled && !req.SkipAckGate) {
                    var gateInstanceId = instance.GetLong(KEY_ID);
                    var pendingAckCount = await _dal.LcAck.CountPendingForInstanceAsync(gateInstanceId, load);
                    if (pendingAckCount > 0) {
                        // Persist any new instance row before returning blocked result.
                        transaction.Commit();
                        committed = true;
                        return new LifeCycleTriggerResult {
                            Applied = false,
                            InstanceGuid = instance.GetString(KEY_GUID) ?? string.Empty,
                            InstanceId = gateInstanceId,
                            Reason = "BlockedByPendingAck",
                            LifecycleAckGuids = Array.Empty<string>(),
                            HookAckGuids = Array.Empty<string>()
                        };
                    }

                    // Blocking hook gate: a blocking hook marks the current state as having an
                    // unfinished business checkpoint. The state machine must not advance until:
                    //   (a) all dispatched blocking hooks for this lifecycle entry are fully ACKed, AND
                    //   (b) no blocking hooks are still waiting to be dispatched (queued but not yet sent).
                    // Note: hook_ack rows are invisible to the lc_ack gate above — this is a separate check.
                    var lastLc = await _dal.LifeCycle.GetLastByInstanceAsync(gateInstanceId, load);
                    if (lastLc != null) {
                        var lastLcId = lastLc.GetLong(KEY_ID);

                        var pendingBlockingHooks = await _dal.Hook.CountPendingBlockingHookAcksAsync(gateInstanceId, lastLcId, load);
                        if (pendingBlockingHooks > 0) {
                            transaction.Commit();
                            committed = true;
                            return new LifeCycleTriggerResult {
                                Applied = false,
                                InstanceGuid = instance.GetString(KEY_GUID) ?? string.Empty,
                                InstanceId = gateInstanceId,
                                Reason = "BlockedByPendingBlockingHook",
                                LifecycleAckGuids = Array.Empty<string>(),
                                HookAckGuids = Array.Empty<string>()
                            };
                        }

                        var undispatchedBlockingHooks = await _dal.Hook.CountUndispatchedBlockingHooksAsync(gateInstanceId, lastLcId, load);
                        if (undispatchedBlockingHooks > 0) {
                            transaction.Commit();
                            committed = true;
                            return new LifeCycleTriggerResult {
                                Applied = false,
                                InstanceGuid = instance.GetString(KEY_GUID) ?? string.Empty,
                                InstanceId = gateInstanceId,
                                Reason = "BlockedByUndispatchedBlockingHook",
                                LifecycleAckGuids = Array.Empty<string>(),
                                HookAckGuids = Array.Empty<string>()
                            };
                        }
                    }
                }

                // Apply transition in transactional context (writes lifecycle rows + instance state).
                transition = await _stateMachine.ApplyTransitionAsync(bp, instance, req.Event, req.Actor, req.Payload, req.OccurredAt, load);

                // Return object is built now and enriched later if transition applies.
                var result = new LifeCycleTriggerResult {
                    Applied = transition.Applied,
                    InstanceGuid = instance.GetString(KEY_GUID) ?? string.Empty,
                    InstanceId = instance.GetLong(KEY_ID),
                    LifeCycleId = transition.LifeCycleId,
                    FromState = bp.StatesById.TryGetValue(transition.FromStateId, out var fs) ? (fs.Name ?? string.Empty) : string.Empty,
                    ToState = bp.StatesById.TryGetValue(transition.ToStateId, out var ts) ? (ts.Name ?? string.Empty) : string.Empty,
                    Reason = transition.Reason ?? string.Empty,
                    LifecycleAckGuids = Array.Empty<string>(),
                    HookAckGuids = Array.Empty<string>()
                };

                if (!transition.Applied) {
                    // Keep any instance creation/update done before transition decision.
                    transaction.Commit();
                    committed = true;
                    return result;
                }

                var hookConsumers = await _ackManager.GetHookConsumersAsync(bp.DefVersionId, ct);
                var normTransitionConsumers = InternalUtils.NormalizeConsumers(transitionConsumers);
                var normHookConsumers = InternalUtils.NormalizeConsumers(hookConsumers);

                var instanceId = result.InstanceId;
                // Link runtime logs from closed state to current lifecycle row for timeline clarity.
                if (transition.LifeCycleId.HasValue && transition.FromStateId > 0) {
                    await _dal.Runtime.StampLcIdByInstanceAndStateAsync(instanceId, transition.FromStateId, transition.LifeCycleId.Value, load);
                }

                // Important: evaluate rules from policy actually attached to this instance.
                var pid = instance.GetLong(KEY_POLICY_ID);
                if (pid > 0) pr = await _policyEnforcer.ResolvePolicyByIdAsync(pid, load);

                // One lifecycle ack_guid is shared across all transition consumers.
                var ackRef = await _ackManager.CreateLifecycleAckAsync(transition.LifeCycleId!.Value, normTransitionConsumers, (int)AckStatus.Pending, load);
                var lcAckGuid = ackRef.AckGuid ?? string.Empty;
                lcAckGuids.Add(lcAckGuid);

                // Resolve per-transition rule context for params + completion event names.
                RuleContext ctx = new();
                if (!string.IsNullOrWhiteSpace(pr.PolicyJson) && bp.StatesById.TryGetValue(transition.ToStateId, out var toState)) {
                    bp.EventsById.TryGetValue(transition.EventId, out var viaEvent);
                    ctx = _policyEnforcer.ResolveRuleContextFromJson(pr.PolicyJson!, toState, viaEvent, ct, pr.PolicyId ?? 0);
                }

                // Base lifecycle payload copied into transition/hook events.
                var lcEvent = new LifeCycleEvent {
                    InstanceGuid = result.InstanceGuid,
                    DefinitionId = bp.DefinitionId,
                    DefinitionVersionId = bp.DefVersionId,
                    EntityId = req.EntityId,
                    OccurredAt = req.OccurredAt ?? DateTimeOffset.UtcNow,
                    AckGuid = lcAckGuid,
                    Metadata = instance.GetString(KEY_METADATA),
                    Params = ctx.Params,
                    OnSuccessEvent = ctx.OnSuccessEvent,
                    OnFailureEvent = ctx.OnFailureEvent
                };

                // Fan out one transition event per consumer.
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

                // Emit hook rows via policy engine and fan out corresponding hook events.
                var hookEmissions = await _policyEnforcer.EmitHooksAsync(bp, instance, transition, load, pr);

                // Now that we know whether hooks exist, set DispatchMode on transition events.
                // NormalRun = no hooks; ValidationMode = hooks present (consumer must not auto-transition).
                var dispatchMode = hookEmissions.Count > 0 ? TransitionDispatchMode.ValidationMode : TransitionDispatchMode.NormalRun;
                foreach (var te in toDispatch.OfType<LifeCycleTransitionEvent>()) {
                    te.DispatchMode = dispatchMode;
                }

                for (var h = 0; h < hookEmissions.Count; h++) {
                    var he = hookEmissions[h];
                    if (hookConsumers.Count < 1) throw new ArgumentException("No Hook consumers found for this definition version. At least one hook consumer is required to proceed.", nameof(req));

                    // Each hook_lc gets its own ack_guid tracked across all hook consumers.
                    var hookAck = await _ackManager.CreateHookAckAsync(he.HookLcId, normHookConsumers, (int)AckStatus.Pending, load);
                    var hookAckGuid = hookAck.AckGuid ?? string.Empty;
                    hookAckGuids.Add(hookAckGuid);
                    await _dal.HookLc.MarkDispatchedAsync(he.HookLcId, load);

                    var runCount = await _dal.HookLc.CountDispatchedByHookIdAsync(he.HookId, load);
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
                            HookType = he.HookType,
                            GroupName = he.GroupName,
                            OrderSeq = he.OrderSeq,
                            AckMode = he.AckMode,
                            RunCount = runCount
                        };
                        toDispatch.Add(hookEvent);
                    }
                }

                // Durability before delivery: commit first, then dispatch.
                transaction.Commit();
                committed = true;

                await _dispatchEventsAsync(toDispatch, ct);

                result.LifecycleAckGuids = lcAckGuids;
                result.HookAckGuids = hookAckGuids;
                return result;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                throw;
            } catch (Exception ex) {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                // Trigger path emits a typed notice, then rethrows for caller visibility.
                _fireNotice(LifeCycleNotice.Error("TRIGGER_ERROR", "TRIGGER_ERROR", ex.Message, ex));
                throw;
            }
        }

        // Reopen flow:
        // 1. Resolve instance and validate terminal flags.
        // 2. Reset to initial state and clear terminal/suspended flags.
        // 3. Find auto-start transition from initial state.
        // 4. If present, reuse TriggerAsync with normal ACKed dispatch and SkipAckGate=true.
        public async Task<LifeCycleTriggerResult> ReopenAsync(string instanceGuid, string actor, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentNullException(nameof(instanceGuid));
            if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentNullException(nameof(actor));

            var load = new DbExecutionLoad(ct);
            var instanceRow = await _dal.Instance.GetByGuidAsync(instanceGuid, load);
            if (instanceRow == null) throw new InvalidOperationException($"Instance not found: {instanceGuid}");

            var instanceId = instanceRow.GetLong(KEY_ID);
            var currentFlags = (uint)instanceRow.GetLong(KEY_FLAGS);
            var entityId = instanceRow.GetString(KEY_ENTITY_ID) ?? string.Empty;
            var defVersionId = instanceRow.GetLong(KEY_DEF_VERSION);

            const uint terminalCheck = (uint)(LifeCycleInstanceFlag.Completed | LifeCycleInstanceFlag.Failed | LifeCycleInstanceFlag.Archived);
            if ((currentFlags & terminalCheck) == 0) {
                return new LifeCycleTriggerResult {
                    Applied = false,
                    InstanceGuid = instanceGuid,
                    InstanceId = instanceId,
                    Reason = "NotTerminal",
                    LifecycleAckGuids = Array.Empty<string>(),
                    HookAckGuids = Array.Empty<string>()
                };
            }

            var bp = await _blueprintManager.GetBlueprintByVersionIdAsync(defVersionId, ct);
            if (bp == null) throw new InvalidOperationException($"Blueprint not found for def_version {defVersionId}.");

            const uint clearMask = (uint)(LifeCycleInstanceFlag.Completed | LifeCycleInstanceFlag.Failed
                                        | LifeCycleInstanceFlag.Archived | LifeCycleInstanceFlag.Suspended);
            await _dal.Instance.ForceResetToStateAsync(instanceId, bp.InitialStateId, clearMask, load);

            var autoStart = bp.Transitions.FirstOrDefault(kv => kv.Key.Item1 == bp.InitialStateId);
            if (autoStart.Value == null) {
                return new LifeCycleTriggerResult {
                    Applied = true,
                    InstanceGuid = instanceGuid,
                    InstanceId = instanceId,
                    ToState = bp.StatesById.TryGetValue(bp.InitialStateId, out var init) ? (init.Name ?? string.Empty) : string.Empty,
                    Reason = "ResetToInitial",
                    LifecycleAckGuids = Array.Empty<string>(),
                    HookAckGuids = Array.Empty<string>()
                };
            }

            bp.EventsById.TryGetValue(autoStart.Value.EventId, out var autoStartEvent);

            return await TriggerAsync(new LifeCycleTriggerRequest {
                DefName = bp.DefName,
                EnvCode = bp.EnvCode,
                EntityId = entityId,
                Event = autoStartEvent?.Name ?? string.Empty,
                Actor = actor,
                SkipAckGate = true
            }, ct);
        }
    }
}
