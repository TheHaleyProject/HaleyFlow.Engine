using Haley.Models;
using Haley.Utils;
using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    public sealed class WorkFlowEngine : IWorkFlowEngine {
        private volatile bool _started;

        public IStateMachine StateMachine { get; }
        public IBlueprintManager? BlueprintManager { get; }
        public IPolicyEnforcer? PolicyEnforcer { get; }
        public IAckManager? AckManager { get; }
        public IInstanceMonitor? InstanceMonitor { get; }
        public IWorkFlowDAL? Dal { get; }

        public event Func<ILifeCycleEvent, Task>? EventRaised;

        public WorkFlowEngine(
            IStateMachine stateMachine,
            IBlueprintManager? blueprintManager = null,
            IPolicyEnforcer? policyEnforcer = null,
            IAckManager? ackManager = null,
            IInstanceMonitor? instanceMonitor = null,
            IWorkFlowDAL? dal = null) {
            StateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            BlueprintManager = blueprintManager;
            PolicyEnforcer = policyEnforcer;
            AckManager = ackManager;
            InstanceMonitor = instanceMonitor;
            Dal = dal;
        }

        public async Task StartAsync(CancellationToken ct = default) {
            if (_started) return;
            _started = true;

            // minimal start behavior: run recovery once if monitor exists
            if (InstanceMonitor != null)
                await InstanceMonitor.RunOnceAsync(ct);
        }

        public Task StopAsync(CancellationToken ct = default) {
            _started = false;
            return Task.CompletedTask;
        }

        public async Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(req.ExternalRef))
                return new LifeCycleTriggerResult { Status = false, Notice = "ExternalRef is required." };

            if (string.IsNullOrWhiteSpace(req.EventName))
                return new LifeCycleTriggerResult { Status = false, Notice = "EventName is required." };

            // Build/resolve blueprint
            LifeCycleBlueprint bp;
            if (req.DefinitionVersionId.HasValue) {
                if (BlueprintManager != null)
                    bp = await BlueprintManager.GetBlueprintByVersionIdAsync(req.DefinitionVersionId.Value, ct);
                else
                    bp = new LifeCycleBlueprint { DefinitionVersionId = req.DefinitionVersionId.Value };
            } else {
                if (BlueprintManager == null)
                    return new LifeCycleTriggerResult { Status = false, Notice = "BlueprintManager is required when DefinitionVersionId is not provided." };

                if (!req.EnvironmentCode.HasValue || string.IsNullOrWhiteSpace(req.DefinitionName))
                    return new LifeCycleTriggerResult { Status = false, Notice = "Either DefinitionVersionId OR (EnvironmentCode + DefinitionName) is required." };

                bp = await BlueprintManager.GetBlueprintLatestAsync(req.EnvironmentCode.Value, req.DefinitionName!, ct);
            }

            var load = new DbExecutionLoad(ct);

            // Ensure instance
            var instance = await StateMachine.EnsureInstanceAsync(bp.DefinitionVersionId, req.ExternalRef, load);

            // Apply transition
            var applied = await StateMachine.ApplyTransitionAsync(
                bp,
                instance,
                req.EventName,
                req.RequestId,
                req.Actor,
                req.Payload,
                load);

            if (!applied.Applied) {
                return new LifeCycleTriggerResult {
                    Status = true,
                    Transitioned = false,
                    InstanceId = applied.InstanceId,
                    LifeCycleId = null,
                    Notice = "Transition not applied."
                };
            }

            var occurredAt = DateTimeOffset.UtcNow;

            // Transition events are not ack-required (for now) => ackGuid empty + AckRequired=false
            // If you later choose to ack transitions, you’ll generate/attach ackGuid here.
            var transitionAckGuid = string.Empty;
            var transitionAckRequired = false;

            // Using your model style: minimal ctor + then fill remaining fields
            var tev = new LifeCycleTransitionEvent(applied.InstanceId, req.ExternalRef, occurredAt, transitionAckGuid) {
                LifeCycleId = applied.LifeCycleId,
                FromStateId = applied.FromStateId,
                ToStateId = applied.ToStateId,
                EventId = applied.EventId,
                EventCode = applied.EventCode,
                EventName = applied.EventName,
                PrevStateMeta = null
            };

            // IMPORTANT: this assumes base LifeCycleEvent exposes a PUBLIC method
            // (not internal) to set context fields with protected setters.
            tev.ApplyContext(
                defVersionId: bp.DefinitionVersionId,
                requestId: req.RequestId,
                ackRequired: transitionAckRequired,
                payload: req.Payload
            );

            // Attach policy JSON (if any) to the transition event
            if (PolicyEnforcer != null) {
                var policy = await PolicyEnforcer.ResolvePolicyAsync(bp, instance, applied, load);
                tev.PolicyId = policy.PolicyId;
                tev.PolicyHash = policy.PolicyHash;
                tev.PolicyJson = policy.PolicyJson;
            }

            await RaiseAsync(tev);

            // Emit + publish hook events (these are ack-required by design)
            if (PolicyEnforcer != null) {
                var hookEvents = await PolicyEnforcer.EmitHooksAsync(bp, instance, applied, load);
                foreach (var he in hookEvents)
                    await RaiseAsync(he);
            }

            return new LifeCycleTriggerResult {
                Status = true,
                Transitioned = true,
                InstanceId = applied.InstanceId,
                LifeCycleId = applied.LifeCycleId
            };
        }

        // UPDATED: ackGuid (string) first-class
        public Task AckAsync(string ackGuid, LifeCycleAckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(ackGuid))
                throw new ArgumentException("ackGuid is required.", nameof(ackGuid));

            if (AckManager == null)
                throw new NotSupportedException("AckManager is not configured on this engine.");

            return AckManager.AckAsync(ackGuid, outcome, message, retryAt, new DbExecutionLoad(ct));
        }

        public Task ClearCacheAsync(CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            BlueprintManager?.Clear();
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(int envCode, string defName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            BlueprintManager?.Invalidate(envCode, defName);
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(long defVersionId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            BlueprintManager?.Invalidate(defVersionId);
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync() {
            await StopAsync();
        }

        private async Task RaiseAsync(ILifeCycleEvent ev) {
            var handlers = EventRaised;
            if (handlers == null) return;

            foreach (var d in handlers.GetInvocationList())
                await ((Func<ILifeCycleEvent, Task>)d).Invoke(ev);
        }
    }

}