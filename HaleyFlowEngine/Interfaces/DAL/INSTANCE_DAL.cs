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
    }

    internal interface IHookRouteDAL {
        Task<long?> GetIdByNameAsync(string name, DbExecutionLoad load = default);
        Task<long> UpsertByNameReturnIdAsync(string name, DbExecutionLoad load = default);
    }

    internal interface IHookGroupDAL {
        Task<long?> GetIdByNameAsync(string name, DbExecutionLoad load = default);
        Task<long> UpsertByNameReturnIdAsync(string name, DbExecutionLoad load = default);
        Task<DbRow?> GetContextByAckGuidAsync(string ackGuid, DbExecutionLoad load = default);
        Task<int> CountUnresolvedInGroupAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long groupId, DbExecutionLoad load = default);
    }

    internal interface IHookDAL {
        Task<DbRow?> GetByIdAsync(long hookId, DbExecutionLoad load = default);
        Task<DbRow?> GetByKeyAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, DbExecutionLoad load = default);
        Task<long> UpsertByKeyReturnIdAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, bool blocking, string? groupName = null, int orderSeq = 1, int ackMode = 0, bool dispatched = true, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceAndStateAsync(long instanceId, long stateId, DbExecutionLoad load = default);
        Task<int> DeleteAsync(long hookId, DbExecutionLoad load = default);
        Task<int> DeleteByInstanceAsync(long instanceId, DbExecutionLoad load = default);

        // Ordered emission support
        Task<DbRow?> GetContextByAckGuidAsync(string ackGuid, DbExecutionLoad load = default);
        Task<int>    CountIncompleteBlockingInOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, int orderSeq, DbExecutionLoad load = default);
        Task<int?>   GetMinUndispatchedOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, DbExecutionLoad load = default);
        Task<DbRows> ListUndispatchedByOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, int orderSeq, DbExecutionLoad load = default);
        Task         MarkDispatchedAsync(long hookId, DbExecutionLoad load = default);
    }

    internal interface ILifeCycleDAL {
        Task<long> InsertAsync(long instanceId, long fromStateId, long toStateId, long eventId, DateTime? occurred = null, DbExecutionLoad load = default);
        Task<DbRow?> GetLastByInstanceAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default);
        Task<DbRows> ListByInstancePagedAsync(long instanceId, int skip, int take, DbExecutionLoad load = default);
        Task<string?> GetTimelineJsonByInstanceIdAsync(long instanceId, DbExecutionLoad load = default);
    }

    internal interface ILifeCycleDataDAL {
        Task<DbRow?> GetByIdAsync(long lifeCycleId, DbExecutionLoad load = default);
        Task<int> UpsertAsync(long lifeCycleId, string? actor, string? payloadJson, DbExecutionLoad load = default);
        Task<int> DeleteAsync(long lifeCycleId, DbExecutionLoad load = default);
    }

}
