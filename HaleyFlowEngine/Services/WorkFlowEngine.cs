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
    internal sealed class WorkFlowEngine :  IWorkFlowEngine {
        private readonly IWorkFlowDAL _dal;
        private readonly LifeCycleEngine _lc;
        public IStateMachine StateMachine { get; }
        public IBlueprintManager? BlueprintManager { get; }
        public IPolicyEnforcer? PolicyEnforcer { get; }
        public IAckManager? AckManager { get; }
        public IRuntimeEngine Runtime { get; }
        public IWorkFlowDAL? Dal { get { return _dal; } }

        public event Func<ILifeCycleEvent, Task>? EventRaised;

        public WorkFlowEngine(IWorkFlowDAL dal, Func<long, long, CancellationToken, Task<IReadOnlyList<long>>>? transitionConsumers = null, Func<long, long, string, CancellationToken, Task<IReadOnlyList<long>>>? hookConsumers = null, IReadOnlyList<long>? monitorConsumers = null) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));

            BlueprintManager = new BlueprintManager(_dal);
            StateMachine = new StateMachine(_dal, BlueprintManager);
            PolicyEnforcer = new PolicyEnforcer(_dal);
            AckManager = new AckManager(_dal, transitionConsumers, hookConsumers);
            Runtime = new RuntimeEngine(_dal);

            _lc = new LifeCycleEngine(_dal, StateMachine, BlueprintManager, PolicyEnforcer, AckManager, monitorConsumers);

            _lc.EventRaised += async (e) => {
                var h = EventRaised;
                if (h != null) await h.Invoke(e);
            };
        }

        public Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) { return _lc.TriggerAsync(req, ct); }

        public Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) { return _lc.AckAsync(consumerId, ackGuid, outcome, message, retryAt, ct); }

        public Task ClearCacheAsync(CancellationToken ct = default) { return _lc.ClearCacheAsync(ct); }

        public Task InvalidateAsync(int envCode, string defName, CancellationToken ct = default) { return _lc.InvalidateAsync(envCode, defName, ct); }

        public Task InvalidateAsync(long defVersionId, CancellationToken ct = default) { return _lc.InvalidateAsync(defVersionId, ct); }

        public Task RunMonitorOnceAsync(CancellationToken ct = default) { return _lc.RunMonitorOnceAsync(ct); }

        public Task StartAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }

        public Task StopAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }

        public ValueTask DisposeAsync() { return _dal.DisposeAsync(); }
    }
}