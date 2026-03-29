using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IInstanceDAL {
        Task<DbRow?> GetByGuidAsync(string guid, DbExecutionLoad load = default);
        Task<long?> GetIdByGuidAsync(string guid, DbExecutionLoad load = default);

        Task<DbRow?> GetByDefIdAndEntityIdAsync(long defId, string entityId, DbExecutionLoad load = default);
        Task<long?> GetIdByKeyAsync(long defVersionId, string entityId, DbExecutionLoad load = default);
        Task<string?> UpsertByKeyReturnGuidAsync(long defVersionId, string entityId, long currentStateId, long? lastEventId, long policyId, uint flags, string? metadata, DbExecutionLoad load = default);
        Task<int> UpdateCurrentStateCasAsync(long instanceId, long expectedFromStateId, long newToStateId, long? lastEventId, DbExecutionLoad load = default);

        Task<int> SetPolicyAsync(long instanceId, long policyId, DbExecutionLoad load = default);
        Task<int> AddFlagsAsync(long instanceId, uint flags, DbExecutionLoad load = default);
        Task<int> RemoveFlagsAsync(long instanceId, uint flags, DbExecutionLoad load = default);
        Task<int> ForceResetToStateAsync(long instanceId, long stateId, uint clearFlagsMask, DbExecutionLoad load = default);

        Task<int> SetMessageAsync(long instanceId, string? message, DbExecutionLoad load = default);
        Task<int> ClearMessageAsync(long instanceId, DbExecutionLoad load = default);
        Task<int> SetContextAsync(long instanceId, string? context, DbExecutionLoad load = default);

        Task<int> SuspendWithMessageAsync(long instanceId, uint flags, string? message, DbExecutionLoad load = default);
        Task<int> FailWithMessageAsync(long instanceId, uint flags, string? message, DbExecutionLoad load = default);
        Task<int> CompleteWithMessageAsync(long instanceId, uint flags, string? message, DbExecutionLoad load = default);
        Task<int> ArchiveWithMessageAsync(long instanceId, uint flags, string? message, DbExecutionLoad load = default);
        Task<int> UnsetFlagsAsync(long instanceId, uint flags, DbExecutionLoad load = default); // (flags & ~FLAGS) query
        Task<DbRows> ListStaleByDefaultStateDurationPagedAsync(int staleSeconds, int processedAckStatus, uint excludedInstanceFlagsMask, int skip, int take, DbExecutionLoad load = default);
        Task<DbRows> ListByFlagsAndDefVersionPagedAsync(long defVersionId, uint flagsMask, int skip, int take, DbExecutionLoad load = default);
        Task<DbRows> ListByEnvDefAndStatusPagedAsync(int envCode, string? defName, uint statusFlags, int skip, int take, DbExecutionLoad load = default);
        Task<DbRows> ListByEnvAndDefPagedAsync(int envCode, string? defName, bool runningOnly, int skip, int take, DbExecutionLoad load = default);
    }

    internal interface IHookRouteDAL {
        Task<long?> GetIdByNameAsync(string name, DbExecutionLoad load = default);
        Task<long> UpsertByNameReturnIdAsync(string name, DbExecutionLoad load = default);
    }

    internal interface IHookGroupDAL {
        Task<long?> GetIdByNameAsync(string name, DbExecutionLoad load = default);
        Task<long> UpsertByNameReturnIdAsync(string name, DbExecutionLoad load = default);
        Task<DbRow?> GetContextByAckGuidAsync(string ackGuid, DbExecutionLoad load = default);
        Task<int> CountUnresolvedInGroupAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, long groupId, DbExecutionLoad load = default);
    }

    internal interface IHookDAL {
        Task<DbRow?> GetByIdAsync(long hookId, DbExecutionLoad load = default);
        Task<DbRow?> GetByKeyAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, DbExecutionLoad load = default);
        // dispatched param removed — dispatch state now lives on hook_lc rows.
        Task<long> UpsertByKeyReturnIdAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, HookType hookType, string? groupName = null, int orderSeq = 1, int ackMode = 0, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceAndStateAsync(long instanceId, long stateId, DbExecutionLoad load = default);
        Task<int> DeleteAsync(long hookId, DbExecutionLoad load = default);
        Task<int> DeleteByInstanceAsync(long instanceId, DbExecutionLoad load = default);

        // Ordered emission support — all order queries now require lcId to scope to the current lifecycle entry.
        Task<DbRow?> GetContextByAckGuidAsync(string ackGuid, DbExecutionLoad load = default);
        Task<DbRow?> GetContextByLcIdAsync(long lcId, DbExecutionLoad load = default);
        Task<int>    CountIncompleteBlockingInOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, int orderSeq, DbExecutionLoad load = default);
        Task<int?>   GetMinUndispatchedOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, DbExecutionLoad load = default);
        Task<DbRows> ListUndispatchedByOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, int orderSeq, DbExecutionLoad load = default);

        // Blocking hook gate — instance-wide checks used by InstanceOrchestrator before accepting a new transition.
        /// <summary>Count dispatched blocking hooks for the given lifecycle entry whose ack_consumer rows are not yet terminal (ack_mode-aware).</summary>
        Task<int> CountPendingBlockingHookAcksAsync(long instanceId, long lcId, DbExecutionLoad load = default);
        /// <summary>Count blocking hooks for the given lifecycle entry that have not been dispatched yet (hook_lc.dispatched=0).</summary>
        Task<int> CountUndispatchedBlockingHooksAsync(long instanceId, long lcId, DbExecutionLoad load = default);
        /// <summary>
        /// Bulk-sets all non-terminal ack_consumer rows for blocking hooks in the given lifecycle entry
        /// to Cancelled (status=5), clearing next_due. Called by the monitor before a timeout transition
        /// so that open hook ACKs are properly closed before the state machine advances.
        /// </summary>
        Task<int> CancelPendingBlockingHookAckConsumersAsync(long instanceId, long lcId, DbExecutionLoad load = default);
        /// <summary>
        /// Bulk-sets all non-terminal ack_consumer rows for hooks in the given lifecycle entry
        /// to Cancelled (status=5), clearing next_due. Used when a gate failure closes the
        /// whole remaining hook plan so late effect ACKs cannot advance the old lifecycle.
        /// </summary>
        Task<int> CancelPendingHookAckConsumersAsync(long instanceId, long lcId, DbExecutionLoad load = default);

        /// <summary>
        /// Marks all undispatched gate hook_lc rows (type=1, dispatched=0) for the given lifecycle scope
        /// as Skipped (status=2, dispatched=1). Called when a gate ACKs success with OnSuccessEvent,
        /// so remaining gates are bypassed and only effect hooks run before firing the success code.
        /// </summary>
        Task<int> SkipUndispatchedGateHooksAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, DbExecutionLoad load = default);

        /// <summary>
        /// Returns route, state_id, via_event, on_entry for the first (lowest order_seq) skipped gate
        /// in the given lifecycle scope. Used after effect drain to re-resolve the gate's OnSuccessEvent.
        /// Returns null if no skipped gate exists (normal flow, no gate-success drain in progress).
        /// </summary>
        Task<DbRow?> GetFirstSkippedGateRouteAsync(long instanceId, long lcId, DbExecutionLoad load = default);
    }

    internal interface IHookLcDAL {
        /// <summary>Creates a hook_lc row linking a hook definition to a lifecycle entry. Idempotent.</summary>
        Task<long> InsertReturnIdAsync(long hookId, long lcId, DbExecutionLoad load = default);
        /// <summary>Marks a hook_lc row as dispatched (ACK rows created, event fired).</summary>
        Task MarkDispatchedAsync(long hookLcId, DbExecutionLoad load = default);
        /// <summary>Counts hook_lc rows for a lifecycle entry that are still waiting for release.</summary>
        Task<int> CountUndispatchedByLcIdAsync(long lcId, DbExecutionLoad load = default);
        /// <summary>Marks every undispatched hook_lc row in a lifecycle entry as Skipped to close that hook plan.</summary>
        Task<int> SkipUndispatchedByLcIdAsync(long lcId, DbExecutionLoad load = default);
        /// <summary>Returns how many times this hook has been fully dispatched (run count across all lifecycle entries).</summary>
        Task<int> CountDispatchedByHookIdAsync(long hookId, DbExecutionLoad load = default);
    }

    internal interface ILifeCycleDAL {
        Task<long> InsertAsync(long instanceId, long fromStateId, long toStateId, long eventId, DateTime? occurred = null, DbExecutionLoad load = default);
        Task<DbRow?> GetContextByLcIdAsync(long lcId, DbExecutionLoad load = default);
        Task<DbRow?> GetLastByInstanceAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstancePagedAsync(long instanceId, int skip, int take, DbExecutionLoad load = default);
        Task<string?> GetTimelineJsonByInstanceIdAsync(long instanceId, DbExecutionLoad load = default);
        // TimelineBuilder queries — small focused fetches assembled in C#.
        Task<DbRow?> GetInstanceForTimelineAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListLifecyclesForTimelineAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListActivitiesForTimelineAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListHooksForTimelineAsync(long instanceId, DbExecutionLoad load = default);
    }

    internal interface ILifeCycleDataDAL {
        Task<DbRow?> GetByIdAsync(long lifeCycleId, DbExecutionLoad load = default);
        Task<int> UpsertAsync(long lifeCycleId, string? actor, string? payloadJson, DbExecutionLoad load = default);
        Task<int> DeleteAsync(long lifeCycleId, DbExecutionLoad load = default);
    }

}

