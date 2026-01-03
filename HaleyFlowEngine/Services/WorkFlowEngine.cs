using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    internal sealed class DefaultWorkFlowEngine : IWorkFlowEngine {
        private readonly IWorkFlowDAL _dal;

        public IStateMachine StateMachine { get; private set; }
        public IBlueprintManager BlueprintManager { get; private set; }
        public IPolicyEnforcer PolicyEnforcer { get; private set; }
        public IAckManager AckManager { get; private set; }
        public IInstanceMonitor InstanceMonitor { get; private set; }
        public IConsumerRegistry ConsumerRegistry { get; private set; }
        public IWorkFlowDAL Dal { get { return _dal; } }

        public event Func<ILifeCycleEvent, Task> EventRaised;

        public DefaultWorkFlowEngine(IWorkFlowDAL dal, IConsumerRegistry consumerRegistry = null) {
            _dal = dal;
            ConsumerRegistry = consumerRegistry;
            BlueprintManager = new DefaultBlueprintManager(dal);
            StateMachine = new DefaultStateMachine(dal, BlueprintManager);
            PolicyEnforcer = new DefaultPolicyEnforcer(dal);
            AckManager = new DefaultAckManager(dal);
            var lc = new DefaultLifeCycleEngine(dal, StateMachine, BlueprintManager, PolicyEnforcer, AckManager, consumerRegistry);
            lc.EventRaised += async (e) => { var h = EventRaised; if (h != null) await h.Invoke(e); };
            _lifeCycle = lc;
            InstanceMonitor = null; // implement once you finalize timeout queries
        }

        private readonly DefaultLifeCycleEngine _lifeCycle;

        public Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) { return _lifeCycle.TriggerAsync(req, ct); }
        public Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) { return _lifeCycle.AckAsync(consumerId, ackGuid, outcome, message, retryAt, ct); }
        public Task ClearCacheAsync(CancellationToken ct = default) { return _lifeCycle.ClearCacheAsync(ct); }
        public Task InvalidateAsync(int envCode, string defName, CancellationToken ct = default) { return _lifeCycle.InvalidateAsync(envCode, defName, ct); }
        public Task InvalidateAsync(long defVersionId, CancellationToken ct = default) { return _lifeCycle.InvalidateAsync(defVersionId, ct); }

        public Task StartAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { return _dal.DisposeAsync(); }
    }

}