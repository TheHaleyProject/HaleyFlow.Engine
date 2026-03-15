using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.KeyConstants;

namespace Haley.Services.Orchestrators {
    // Handles all administrative instance state mutations that do NOT go through the
    // normal trigger pipeline (those live in TriggerOrchestrator / ReopenOrchestrator).
    //
    // Operations owned here:
    //
    //   SuspendAsync    — pause an Active instance (admin-initiated).
    //                     Flags: set Suspended, clear nothing.  No ack budget changes.
    //
    //   ResumeAsync     — lift an admin-suspend.
    //                     Flags: clear Suspended, set Active.  No ack budget changes.
    //                     Use only for suspensions caused by SuspendAsync (not by max-retry).
    //
    //   FailAsync       — mark an Active instance as Failed without a transition.
    //                     Flags: set Failed, clear Active.  current_state is preserved.
    //
    //   UnsuspendAsync  — lift a monitor-triggered suspension (ack max-retry exhausted).
    //                     Extends per-row ack retry budget:
    //                       max_trigger = trigger_count + globalMaxRetryCount
    //                       status      → Pending
    //                       next_due    → UTC_TIMESTAMP()
    //                     trigger_count is NEVER reset — it is a monotonically increasing
    //                     audit counter. Only after budget extension are the flags flipped:
    //                     clear Suspended, set Active.
    internal sealed class MaintenanceOrchestrator {
        private readonly IWorkFlowDAL _dal;
        private readonly int _maxRetryCount;

        public MaintenanceOrchestrator(IWorkFlowDAL dal, int maxRetryCount) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _maxRetryCount = maxRetryCount > 0 ? maxRetryCount : 10;
        }

        // Pause an in-progress (Active) instance without applying a transition.
        // Terminal instances and already-suspended instances are rejected.
        public async Task<bool> SuspendAsync(string instanceGuid, string? message, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentException("instanceGuid is required.", nameof(instanceGuid));

            var load = new DbExecutionLoad(ct);
            var row = await _dal.Instance.GetByGuidAsync(instanceGuid.Trim(), load);
            if (row == null) return false;

            var instanceId = row.GetLong(KEY_ID);
            if (instanceId <= 0) return false;

            var flags = (uint)row.GetLong(KEY_FLAGS);
            if ((flags & (uint)LifeCycleInstanceFlag.Completed) != 0)
                throw new InvalidOperationException("Cannot suspend a completed instance.");
            if ((flags & (uint)LifeCycleInstanceFlag.Archived) != 0)
                throw new InvalidOperationException("Cannot suspend an archived instance.");
            if ((flags & (uint)LifeCycleInstanceFlag.Failed) != 0)
                throw new InvalidOperationException("Cannot suspend a failed instance.");
            if ((flags & (uint)LifeCycleInstanceFlag.Suspended) != 0)
                throw new InvalidOperationException("Instance is already suspended.");
            if ((flags & (uint)LifeCycleInstanceFlag.Active) == 0)
                throw new InvalidOperationException("Only in-progress (Active) instances can be suspended.");

            var affected = await _dal.Instance.SuspendWithMessageAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended,
                string.IsNullOrWhiteSpace(message) ? null : message.Trim(), load);
            return affected > 0;
        }

        // Lift an admin-initiated suspension (flags only — no ack budget change).
        // Use ResumeAsync for suspensions created by SuspendAsync.
        // Use UnsuspendAsync for suspensions triggered by the monitor (ack max-retry exhausted).
        public async Task<bool> ResumeAsync(string instanceGuid, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentException("instanceGuid is required.", nameof(instanceGuid));

            var load = new DbExecutionLoad(ct);
            var row = await _dal.Instance.GetByGuidAsync(instanceGuid.Trim(), load);
            if (row == null) return false;

            var instanceId = row.GetLong(KEY_ID);
            if (instanceId <= 0) return false;

            var flags = (uint)row.GetLong(KEY_FLAGS);
            if ((flags & (uint)LifeCycleInstanceFlag.Completed) != 0)
                throw new InvalidOperationException("Cannot resume a completed instance.");
            if ((flags & (uint)LifeCycleInstanceFlag.Archived) != 0)
                throw new InvalidOperationException("Cannot resume an archived instance.");
            if ((flags & (uint)LifeCycleInstanceFlag.Failed) != 0)
                throw new InvalidOperationException("Cannot resume a failed instance. Reopen instead.");
            if ((flags & (uint)LifeCycleInstanceFlag.Suspended) == 0)
                throw new InvalidOperationException("Instance is not suspended.");

            var affected = await _dal.Instance.UnsetFlagsAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended, load);
            await _dal.Instance.AddFlagsAsync(instanceId, (uint)LifeCycleInstanceFlag.Active, load);
            await _dal.Instance.ClearMessageAsync(instanceId, load);
            return affected > 0;
        }

        // Mark an Active instance as Failed without applying a transition.
        // Sets Failed and clears Active; current_state and transition history are unchanged.
        public async Task<bool> FailAsync(string instanceGuid, string? message, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentException("instanceGuid is required.", nameof(instanceGuid));

            var load = new DbExecutionLoad(ct);
            var row = await _dal.Instance.GetByGuidAsync(instanceGuid.Trim(), load);
            if (row == null) return false;

            var instanceId = row.GetLong(KEY_ID);
            if (instanceId <= 0) return false;

            var flags = (uint)row.GetLong(KEY_FLAGS);
            if ((flags & (uint)LifeCycleInstanceFlag.Completed) != 0)
                throw new InvalidOperationException("Cannot fail a completed instance.");
            if ((flags & (uint)LifeCycleInstanceFlag.Archived) != 0)
                throw new InvalidOperationException("Cannot fail an archived instance.");
            if ((flags & (uint)LifeCycleInstanceFlag.Failed) != 0)
                throw new InvalidOperationException("Instance is already failed.");
            if ((flags & (uint)LifeCycleInstanceFlag.Active) == 0)
                throw new InvalidOperationException("Only pending/in-progress (Active) instances can be marked failed.");

            var affected = await _dal.Instance.FailWithMessageAsync(instanceId, (uint)LifeCycleInstanceFlag.Failed,
                string.IsNullOrWhiteSpace(message) ? null : message.Trim(), load);
            return affected > 0;
        }

        // Lift a monitor-triggered suspension by extending the per-row ACK retry budget.
        // Sets max_trigger = trigger_count + globalMaxRetryCount for every Failed ack_consumer
        // row on this instance (both lc and hook), resets those rows to Pending/next_due=now,
        // then clears Suspended and restores Active.
        public async Task<bool> UnsuspendAsync(string instanceGuid, string actor, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentNullException(nameof(instanceGuid));

            var load = new DbExecutionLoad(ct);
            var row = await _dal.Instance.GetByGuidAsync(instanceGuid.Trim(), load);
            if (row == null) return false;

            var instanceId = row.GetLong(KEY_ID);
            if (instanceId <= 0) return false;

            var flags = (uint)row.GetLong(KEY_FLAGS);
            if ((flags & (uint)LifeCycleInstanceFlag.Suspended) == 0)
                throw new InvalidOperationException("Instance is not suspended.");
            if ((flags & (uint)LifeCycleInstanceFlag.Completed) != 0)
                throw new InvalidOperationException("Cannot unsuspend a completed instance.");
            if ((flags & (uint)LifeCycleInstanceFlag.Archived) != 0)
                throw new InvalidOperationException("Cannot unsuspend an archived instance.");

            await _dal.AckConsumer.ExtendLcBudgetByInstanceIdAsync(instanceId, _maxRetryCount, load);
            await _dal.AckConsumer.ExtendHookBudgetByInstanceIdAsync(instanceId, _maxRetryCount, load);

            var affected = await _dal.Instance.UnsetFlagsAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended, load);
            await _dal.Instance.AddFlagsAsync(instanceId, (uint)LifeCycleInstanceFlag.Active, load);
            await _dal.Instance.ClearMessageAsync(instanceId, load);
            return affected > 0;
        }
    }
}
