using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IStateMachine {
        Task<DbRow> EnsureInstanceAsync(long defVersionId, string entityId, long policyId, string? metadata, DbExecutionLoad load = default);
        Task<ApplyTransitionResult> ApplyTransitionAsync(LifeCycleBlueprint bp, DbRow instance, string eventName, string? actor, IReadOnlyDictionary<string, object?>? payload, DateTimeOffset? occurredAt = null, DbExecutionLoad load = default);
        Task<int> SetInstanceMessageAsync(long instanceId, string? message, DbExecutionLoad load = default);
        Task<int> ClearInstanceMessageAsync(long instanceId, DbExecutionLoad load = default);
        Task<int> SetInstanceFlagsWithMessageAsync(long instanceId, uint flagsToSet, string? message, DbExecutionLoad load = default);   // uses SUSPEND/FAIL/COMPLETE/ARCHIVE query with FLAGS
        Task<int> UnsetInstanceFlagsAsync(long instanceId, uint flagsToClear, DbExecutionLoad load = default);                         // uses (flags & ~FLAGS)
    }
}
