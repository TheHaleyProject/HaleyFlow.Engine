using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IRuntimeEngine {
        Task<long> UpsertAsync(RuntimeLogByIdRequest req, CancellationToken ct = default);
        Task<int> SetStatusAsync(long runtimeId, long statusId, CancellationToken ct = default);
        Task<int> SetStatusAsync(long runtimeId, string status, CancellationToken ct = default);
        Task<int> SetFrozenAsync(long runtimeId, bool frozen, CancellationToken ct = default);
        Task<int> SetLcIdAsync(long runtimeId, long lcId, CancellationToken ct = default);
        Task<long> EnsureActivityAsync(string displayName, CancellationToken ct = default);
        Task<long> EnsureActivityStatusAsync(string displayName, CancellationToken ct = default);
    }
}
