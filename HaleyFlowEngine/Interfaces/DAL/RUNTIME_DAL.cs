using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IRuntimeDAL {
        Task<DbRow?> GetByIdAsync(long runtimeId, DbExecutionLoad load = default);
        Task<DbRow?> GetByKeyAsync(long instanceId, long activityId, long stateId, string actorId, DbExecutionLoad load = default);
        Task<long> UpsertByKeyReturnIdAsync(long instanceId, long activityId, long stateId, string actorId, long statusId, long lcId, bool frozen, DbExecutionLoad load = default);
        Task<int> SetStatusAsync(long runtimeId, long statusId, DbExecutionLoad load = default);
        Task<int> SetFrozenAsync(long runtimeId, bool frozen, DbExecutionLoad load = default);
        Task<int> SetLcIdAsync(long runtimeId, long lcId, DbExecutionLoad load = default);
        Task<int> StampLcIdByInstanceAndStateAsync(long instanceId, long stateId, long lcId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceAndStateAsync(long instanceId, long stateId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceStateActivityAsync(long instanceId, long stateId, long activityId, DbExecutionLoad load = default);
        Task<DbRows> ListByLifeCycleAsync(long lcId, DbExecutionLoad load = default);
    }

    internal interface IRuntimeDataDAL {
        Task<DbRow?> GetByIdAsync(long runtimeId, DbExecutionLoad load = default);
        Task<int> UpsertAsync(long runtimeId, string? dataJson, string? payloadJson, DbExecutionLoad load = default);
        Task<int> DeleteAsync(long runtimeId, DbExecutionLoad load = default);
    }

    internal interface IActivityDAL {
        Task<DbRows> ListAllAsync(DbExecutionLoad load = default);
        Task<DbRow?> GetByIdAsync(long id, DbExecutionLoad load = default);
        Task<DbRow?> GetByNameAsync(string name, DbExecutionLoad load = default);
        Task<long> InsertAsync(string displayName, DbExecutionLoad load = default);
    }

    internal interface IActivityStatusDAL {
        Task<DbRows> ListAllAsync(DbExecutionLoad load = default);
        Task<DbRow?> GetByIdAsync(long id, DbExecutionLoad load = default);
        Task<DbRow?> GetByNameAsync(string name, DbExecutionLoad load = default);
        Task<long> InsertAsync(string displayName, DbExecutionLoad load = default);
    }

    internal interface ILifeCycleTimeoutDAL {
        // Case A (timeout_event IS set): idempotency marker only. ON DUPLICATE KEY noop.
        Task<int> InsertCaseAAsync(long lcId, int? maxRetry, DbExecutionLoad load = default);
        // Case B (no timeout_event): first-occurrence insert. trigger_count=1, next_due=now+staleSeconds.
        Task<int> InsertCaseBFirstAsync(long lcId, int? maxRetry, int staleSeconds, DbExecutionLoad load = default);
        // Case B subsequent tick: increment trigger_count, refresh last_trigger and next_due.
        Task<int> UpdateCaseBNextAsync(long lcId, int staleSeconds, DbExecutionLoad load = default);
        // Due Case A entries: event_code IS NOT NULL, no lc_timeout row yet, initial grace elapsed.
        Task<DbRows> ListDueCaseAPagedAsync(uint excludedInstanceFlagsMask, int skip, int take, DbExecutionLoad load = default);
        // Due Case B entries: no event_code, either initial grace elapsed or next_due passed.
        Task<DbRows> ListDueCaseBPagedAsync(uint excludedInstanceFlagsMask, int skip, int take, DbExecutionLoad load = default);
    }

    internal interface ILcNextDAL {
        Task<int> InsertAsync(long lcId, int nextEvent, long? sourceAckId, DbExecutionLoad load = default);
        Task<int> MarkDispatchedAsync(long lcId, long ackId, DbExecutionLoad load = default);
        Task<DbRows> ListPendingAsync(int take, DbExecutionLoad load = default);
    }
}
