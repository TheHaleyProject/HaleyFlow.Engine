using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IAckManager {
        Task<IReadOnlyList<long>> GetTransitionConsumersAsync(long defVersionId, CancellationToken ct = default);
        Task<IReadOnlyList<long>> GetHookConsumersAsync(long defVersionId, CancellationToken ct = default);
        Task<IWorkFlowAckRef> CreateLifecycleAckAsync(long lifecycleId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default);
        Task<IWorkFlowAckRef> CreateHookAckAsync(long hookId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default);
        Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, DbExecutionLoad load = default);
        Task MarkRetryAsync(long ackId, long consumerId, DateTimeOffset? retryAt = null, DbExecutionLoad load = default);
        Task SetStatusAsync(long ackId, long consumerId, int ackStatus, DbExecutionLoad load = default);
        Task<IReadOnlyList<ILifeCycleDispatchItem>> ListDueLifecycleDispatchAsync(long consumerId, int ackStatus, int ttlSeconds, int skip, int take, DbExecutionLoad load = default);
        Task<IReadOnlyList<ILifeCycleDispatchItem>> ListDueHookDispatchAsync(long consumerId, int ackStatus, int ttlSeconds, int skip, int take, DbExecutionLoad load = default);
        Task<int> CountDueLifecycleDispatchAsync(int ackStatus, DbExecutionLoad load = default);
        Task<int> CountDueHookDispatchAsync(int ackStatus, DbExecutionLoad load = default);
    }

}
