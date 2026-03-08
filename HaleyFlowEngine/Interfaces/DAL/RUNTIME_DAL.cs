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
        Task<DbRows> ListDuePagedAsync(uint excludedInstanceFlagsMask, int skip, int take, DbExecutionLoad load = default);
        Task<int> InsertIgnoreAsync(long entryLcId, DbExecutionLoad load = default);
    }
}
