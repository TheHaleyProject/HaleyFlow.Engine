using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;

namespace Haley.Services {
    // EngineCare is the service layer for engine health and maintenance queries.
    // It is constructed by WorkFlowEngine and holds both:
    //   IEngineCareDAL  — for consumer-liveness and stale-instance counts (QRY_ENGINE_HEALTH)
    //   IAckManager     — for pending/delivered ACK counts (already owns those queries)
    // WorkFlowEngine.GetHealthAsync calls Care.GetHealthAsync and then stamps the two
    // non-DB fields (IsMonitorRunning, MonitorInterval) on the returned object.
    internal sealed class EngineCare : IEngineCare {
        private readonly IEngineCareDAL _dal;
        private readonly IAckManager _ackManager;

        public EngineCare(IEngineCareDAL dal, IAckManager ackManager) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _ackManager = ackManager ?? throw new ArgumentNullException(nameof(ackManager));
        }

        public async Task<WorkFlowEngineHealth> GetHealthAsync(int ttlSeconds, TimeSpan defaultStateStaleDuration, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var load = new DbExecutionLoad(ct);

            // ACK counts — delegated to AckManager which already owns these queries.
            var dueLifecyclePending  = await _ackManager.CountDueLifecycleDispatchAsync((int)AckStatus.Pending,   load);
            var dueLifecycleDelivered = await _ackManager.CountDueLifecycleDispatchAsync((int)AckStatus.Delivered, load);
            var dueHookPending       = await _ackManager.CountDueHookDispatchAsync((int)AckStatus.Pending,         load);
            var dueHookDelivered     = await _ackManager.CountDueHookDispatchAsync((int)AckStatus.Delivered,       load);

            // Consumer liveness — via IEngineCareDAL (QRY_ENGINE_HEALTH).
            var totalConsumers = await _dal.CountConsumersTotalAsync(load);
            var aliveConsumers = await _dal.CountConsumersAliveAsync(ttlSeconds, load);
            var downConsumers  = await _dal.CountConsumersDownAsync(ttlSeconds, load);

            // Stale-instance count — only when the duration is configured.
            var staleCount = 0L;
            if (defaultStateStaleDuration > TimeSpan.Zero) {
                var staleSeconds = (int)Math.Max(1, defaultStateStaleDuration.TotalSeconds);
                var processed = (int)AckStatus.Processed;
                var excluded  = (uint)(LifeCycleInstanceFlag.Suspended | LifeCycleInstanceFlag.Completed
                                     | LifeCycleInstanceFlag.Failed    | LifeCycleInstanceFlag.Archived);
                staleCount = await _dal.CountStaleDefaultStateAsync(excluded, processed, staleSeconds, load);
            }

            // IsMonitorRunning and MonitorInterval are set by WorkFlowEngine after this call
            // (they are not DB-derived — they come from the monitor's runtime state and options).
            return new WorkFlowEngineHealth {
                UtcNow                   = DateTimeOffset.UtcNow,
                ConsumerTtlSeconds       = ttlSeconds,
                DueLifecyclePendingCount  = dueLifecyclePending,
                DueLifecycleDeliveredCount = dueLifecycleDelivered,
                DueHookPendingCount      = dueHookPending,
                DueHookDeliveredCount    = dueHookDelivered,
                TotalConsumers           = (int)totalConsumers,
                AliveConsumers           = (int)aliveConsumers,
                DownConsumers            = (int)downConsumers,
                DefaultStateStaleCount   = (int)staleCount
            };
        }

        public async Task<WorkFlowEngineSummary> GetSummaryAsync(int envCode, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var load = new DbExecutionLoad(ct);
            var total   = await _dal.CountTotalInstancesByEnvAsync(envCode, load);
            var running = await _dal.CountRunningInstancesByEnvAsync(envCode, load);
            var pending = await _dal.CountPendingAcksAsync(load);
            return new WorkFlowEngineSummary {
                EnvCode          = envCode,
                TotalInstances   = total,
                RunningInstances = running,
                PendingAcks      = pending
            };
        }
    }
}
