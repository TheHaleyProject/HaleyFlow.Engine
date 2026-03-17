using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IAckDAL {
        Task<DbRow?> GetByIdAsync(long ackId, DbExecutionLoad load = default);
        Task<DbRow?> GetByGuidAsync(string guid, DbExecutionLoad load = default);
        Task<long?> GetIdByGuidAsync(string guid, DbExecutionLoad load = default);

        Task<DbRow?> InsertReturnRowAsync(DbExecutionLoad load = default);
        Task<DbRow?> InsertWithGuidReturnRowAsync(string guid, DbExecutionLoad load = default);
        Task<int> DeleteAsync(long ackId, DbExecutionLoad load = default);
    }

    internal interface IAckConsumerDAL {
        Task<DbRow?> GetByKeyAsync(long ackId, long consumer, DbExecutionLoad load = default);
        Task<DbRow?> GetByAckGuidAndConsumerAsync(string ackGuid, long consumer, DbExecutionLoad load = default);
        Task<int> UpsertByAckIdAndConsumerAsync(long ackId, long consumer, int status, DateTime? utcNextDue, int maxTrigger, DbExecutionLoad load = default);
        Task<int> SetStatusAndDueAsync(long ackId, long consumer, int status, DateTime? utcNextDue, string? message = null, DbExecutionLoad load = default);
        Task<int> SetStatusAndDueByGuidAsync(string ackGuid, long consumer, int status, DateTime? utcNextDue, string? message = null, DbExecutionLoad load = default);
        Task<int> MarkTriggerAsync(long ackId, long consumer, DateTime? utcNextDue, DbExecutionLoad load = default);
        Task<DbRows> ListDueByConsumerAndStatusPagedAsync(long consumer, int status, int skip, int take, DbExecutionLoad load = default);
        Task<DbRows> ListDueByStatusPagedAsync(int status, int skip, int take, DbExecutionLoad load = default);
        Task<int> PushNextDueForDownAsync(long consumerId, int ackStatus, int ttlSeconds, int recheckSeconds, DbExecutionLoad load = default);
        // Mark all sibling ack_consumer rows for an ack as Processed (used for ack_mode=Any).
        Task<int> MarkAllProcessedByAckIdAsync(long ackId, DbExecutionLoad load = default);
        Task<DbRows> ListPendingDetailPagedAsync(int envCode, int skip, int take, DbExecutionLoad load = default);
        // Extend retry budget for all Failed lc/hook ack_consumer rows for the given instance.
        // Sets max_trigger = trigger_count + maxTrigger and resets status → Pending so the monitor retries.
        Task<int> ExtendLcBudgetByInstanceIdAsync(long instanceId, int maxTrigger, DbExecutionLoad load = default);
        Task<int> ExtendHookBudgetByInstanceIdAsync(long instanceId, int maxTrigger, DbExecutionLoad load = default);
    }

    internal interface IAckDispatchDAL {
        Task<DbRows> ListDueLifecyclePagedAsync(long consumer, int status, int ttlSeconds, int skip, int take, DbExecutionLoad load = default);
        Task<DbRows> ListDueHookPagedAsync(long consumer, int status, int ttlSeconds, int skip, int take, DbExecutionLoad load = default);
        Task<int?> CountDueLifecycleAsync(int status, DbExecutionLoad load = default);
        Task<int?> CountDueHookAsync(int status, DbExecutionLoad load = default);
    }

    internal interface IHookAckDAL {
        // hook_ack.hook_id now references hook_lc.id — all params named hookLcId for clarity.
        Task<long?> GetAckIdByHookLcIdAsync(long hookLcId, DbExecutionLoad load = default);
        // Resolves state_id from an ack guid via hook_ack → hook_lc → hook. Returns null if not a hook ack.
        Task<long?> GetStateIdByAckGuidAsync(string ackGuid, DbExecutionLoad load = default);
        Task<int> AttachAsync(long ackId, long hookLcId, DbExecutionLoad load = default);
        Task<int> DeleteByHookLcIdAsync(long hookLcId, DbExecutionLoad load = default);
    }

    internal interface ILcAckDAL {
        Task<long?> GetAckIdByLcIdAsync(long lcId, DbExecutionLoad load = default);
        Task<int> AttachAsync(long ackId, long lcId, DbExecutionLoad load = default);
        Task<int> DeleteByLcIdAsync(long lcId, DbExecutionLoad load = default);
        Task<int> CountPendingForInstanceAsync(long instanceId, DbExecutionLoad load = default);
    }
}
