using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.KeyConstants;

namespace Haley.Services.Orchestrators {
    // Encapsulates "reopen terminal instance" workflow.
    // Responsibility:
    // - validate terminal state
    // - reset instance to blueprint initial state
    // - optionally auto-trigger first transition from initial state
    internal sealed class ReopenOrchestrator {
        private readonly IWorkFlowDAL _dal;
        private readonly IBlueprintManager _blueprintManager;
        private readonly Func<LifeCycleTriggerRequest, CancellationToken, Task<LifeCycleTriggerResult>> _triggerAsync;

        // triggerAsync callback reuses existing trigger orchestration without duplicating logic.
        public ReopenOrchestrator(IWorkFlowDAL dal, IBlueprintManager blueprintManager, Func<LifeCycleTriggerRequest, CancellationToken, Task<LifeCycleTriggerResult>> triggerAsync) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _blueprintManager = blueprintManager ?? throw new ArgumentNullException(nameof(blueprintManager));
            _triggerAsync = triggerAsync ?? throw new ArgumentNullException(nameof(triggerAsync));
        }

        // Reopen flow:
        // 1. Resolve instance and validate terminal flags.
        // 2. Reset to initial state and clear terminal/suspended flags.
        // 3. Find auto-start transition from initial state.
        // 4. If present, call TriggerAsync with AckRequired=false and SkipAckGate=true.
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

            // Reopen is intentionally restricted to terminal outcomes.
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

            // Reset state and clear terminal/suspended flags atomically in DAL.
            const uint clearMask = (uint)(LifeCycleInstanceFlag.Completed | LifeCycleInstanceFlag.Failed
                                        | LifeCycleInstanceFlag.Archived | LifeCycleInstanceFlag.Suspended);
            await _dal.Instance.ForceResetToStateAsync(instanceId, bp.InitialStateId, clearMask, load);

            // Convention: first transition whose source is initial state is treated as auto-start.
            var autoStart = bp.Transitions.FirstOrDefault(kv => kv.Key.Item1 == bp.InitialStateId);
            if (autoStart.Value == null) {
                // No auto-start transition configured; reopening ends at initial state reset.
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

            // Reopen-trigger is fire-and-move semantics: do not create ACK gate pressure from this internal call.
            return await _triggerAsync(new LifeCycleTriggerRequest {
                DefName = bp.DefName,
                EnvCode = bp.EnvCode,
                EntityId = entityId,
                Event = autoStartEvent?.Name ?? string.Empty,
                Actor = actor,
                AckRequired = false,
                SkipAckGate = true
            }, ct);
        }
    }
}
