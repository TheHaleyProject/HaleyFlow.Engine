using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IBlueprintManager {
        Task<DbRow> GetLatestDefVersionAsync(int envCode, string defName, CancellationToken ct = default);
        Task<DbRow> GetDefVersionByIdAsync(long defVersionId, CancellationToken ct = default);
        Task<LifeCycleBlueprint> GetBlueprintLatestAsync(int envCode, string defName, CancellationToken ct = default);
        Task<LifeCycleBlueprint> GetBlueprintByVersionIdAsync(long defVersionId, CancellationToken ct = default);
        void Clear();
        void Invalidate(int envCode, string defName);
        void Invalidate(long defVersionId);
        Task<long> EnsureConsumerIdAsync(int envCode, string consumerGuid, CancellationToken ct = default);
        Task<long> ResolveConsumerIdAsync(int envCode, string? consumerGuid, CancellationToken ct = default);
        Task BeatConsumerAsync(int envCode, string consumerGuid, CancellationToken ct = default);
        Task<int> EnsureEnvironmentAsync(int envCode, string? envDisplayName, DbExecutionLoad load);
    }
}
