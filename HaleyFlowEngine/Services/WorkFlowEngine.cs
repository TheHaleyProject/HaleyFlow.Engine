using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Haley.Services {

    public sealed class WorkFlowEngine : IWorkFlowEngine {
        private readonly IWorkFlowDAL _dal;
        private readonly WorkFlowEngineOptions _opt;
        private readonly IReadOnlyList<long> _monitorConsumers;
        private readonly TimeSpan _ackPendingResendAfter = TimeSpan.FromSeconds(30);  // TODO: move to options
        private readonly TimeSpan _ackDeliveredResendAfter = TimeSpan.FromMinutes(5); // TODO: move to options

        public IStateMachine StateMachine { get; }
        public IBlueprintManager BlueprintManager { get; }
        public IBlueprintImporter BlueprintImporter { get; }
        public IPolicyEnforcer PolicyEnforcer { get; }
        public IAckManager AckManager { get; }
        public IRuntimeEngine Runtime { get; }
        public ILifeCycleMonitor Monitor { get; }
        public IWorkFlowDAL Dal { get { return _dal; } }

        public event Func<ILifeCycleEvent, Task>? EventRaised;
        public event Func<LifeCycleNotice, Task>? NoticeRaised;

        public WorkFlowEngine(IWorkFlowDAL dal, WorkFlowEngineOptions? options = null, IReadOnlyList<long>? monitorConsumers = null) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _opt = options ?? new WorkFlowEngineOptions();

            BlueprintManager = _opt.BlueprintManager ?? new BlueprintManager(_dal);

            // If you don't want engine to auto-create importer, pass it via options.
            BlueprintImporter = _opt.BlueprintImporter ?? throw new InvalidOperationException("BlueprintImporter is required (set WorkFlowEngineOptions.BlueprintImporter).");

            StateMachine = _opt.StateMachine ?? new StateMachine(_dal, BlueprintManager);
            PolicyEnforcer = _opt.PolicyEnforcer ?? new PolicyEnforcer(_dal);

            var tc = _opt.ResolveTransitionConsumers;
            var hc = _opt.ResolveHookConsumers;
            Func<long, long, CancellationToken, Task<IReadOnlyList<long>>> transitionConsumers = (dv, iid, ct) => Task.FromResult<IReadOnlyList<long>>(tc?.Invoke(dv, iid) ?? new long[] { _opt.DefaultConsumerId });
            Func<long, long, string, CancellationToken, Task<IReadOnlyList<long>>> hookConsumers = (dv, iid, code, ct) => Task.FromResult<IReadOnlyList<long>>(hc?.Invoke(dv, iid, code) ?? new long[] { _opt.DefaultConsumerId });

            AckManager = _opt.AckManager ?? new AckManager(_dal, transitionConsumers, hookConsumers);
            Runtime = _opt.RuntimeEngine ?? new RuntimeEngine(_dal);

            _monitorConsumers = monitorConsumers ?? new long[] { _opt.DefaultConsumerId };
            Monitor = new LifeCycleMonitor(_opt.MonitorInterval, ct => RunMonitorOnceAsync(ct), (ex, ct) => RaiseNoticeSafeAsync(LifeCycleNotice.Error("MONITOR_ERROR", ex.Message, ex), ct));
        }

        public Task StartAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Monitor.StartAsync(ct); }

        public Task StopAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Monitor.StopAsync(ct); }

        public async ValueTask DisposeAsync() { try { await StopAsync(CancellationToken.None); } catch { } await Monitor.DisposeAsync(); await _dal.DisposeAsync(); }

        public async Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrWhiteSpace(req.DefName)) throw new ArgumentNullException(nameof(req.DefName));
            if (string.IsNullOrWhiteSpace(req.ExternalRef)) throw new ArgumentNullException(nameof(req.ExternalRef));
            if (string.IsNullOrWhiteSpace(req.Event)) throw new ArgumentNullException(nameof(req.Event));

            // Blueprint read can be outside txn (pure read + cached).
            var bp = await BlueprintManager.GetBlueprintLatestAsync(req.EnvCode, req.DefName, ct);

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;

            var toDispatch = new List<ILifeCycleEvent>(8);
            var lcAckGuids = new List<string>(4);
            var hookAckGuids = new List<string>(8);

            DbRow instance = null!;
            ApplyTransitionResult applied = null!;
            PolicyResolution pr = null!;

            try {
                instance = await StateMachine.EnsureInstanceAsync(bp.DefVersionId, req.ExternalRef, load);
                applied = await StateMachine.ApplyTransitionAsync(bp, instance, req.Event, req.RequestId, req.Actor, req.Payload, load);

                var result = new LifeCycleTriggerResult {
                    Applied = applied.Applied,
                    InstanceGuid = instance.GetString("guid") ?? string.Empty,
                    InstanceId = instance.GetLong("id"),
                    LifeCycleId = applied.LifeCycleId,
                    FromState = bp.StatesById.TryGetValue(applied.FromStateId, out var fs) ? (fs.Name ?? string.Empty) : string.Empty,
                    ToState = bp.StatesById.TryGetValue(applied.ToStateId, out var ts) ? (ts.Name ?? string.Empty) : string.Empty,
                    Reason = applied.Reason ?? string.Empty,
                    LifecycleAckGuids = Array.Empty<string>(),
                    HookAckGuids = Array.Empty<string>()
                };

                // Even if transition not applied, instance may have been created/ensured -> commit that.
                if (!applied.Applied) {
                    transaction.Commit();
                    committed = true;
                    return result;
                }

                var instanceId = result.InstanceId;

                // Resolve policy (optional attach)
                pr = await PolicyEnforcer.ResolvePolicyAsync(bp, instance, applied, load) ?? new PolicyResolution();
                if (pr.PolicyId.HasValue) await _dal.Instance.SetPolicyAsync(instanceId, pr.PolicyId.Value, load);

                // Transition consumers
                var transitionConsumers = await AckManager.GetTransitionConsumersAsync(bp.DefVersionId, instanceId, ct);
                var normTransitionConsumers = NormalizeConsumers(transitionConsumers, _opt.DefaultConsumerId);

                // Create lifecycle ACK (one ack guid, multiple consumers) if required
                var lcAckGuid = string.Empty;
                if (req.AckRequired) {
                    var ackRef = await AckManager.CreateLifecycleAckAsync(applied.LifeCycleId!.Value, normTransitionConsumers, (int)AckStatus.Pending, load);
                    lcAckGuid = ackRef.AckGuid ?? string.Empty;
                    lcAckGuids.Add(lcAckGuid);
                }

                // Build transition events (dispatch after commit)
                for (var i = 0; i < normTransitionConsumers.Count; i++) {
                    var consumerId = normTransitionConsumers[i];
                    toDispatch.Add(new LifeCycleTransitionEvent {
                        ConsumerId = consumerId,
                        InstanceId = instanceId,
                        DefinitionVersionId = bp.DefVersionId,
                        ExternalRef = req.ExternalRef,
                        RequestId = req.RequestId,
                        OccurredAt = DateTimeOffset.UtcNow,
                        AckGuid = lcAckGuid,
                        AckRequired = req.AckRequired,
                        Payload = req.Payload,
                        LifeCycleId = applied.LifeCycleId.Value,
                        FromStateId = applied.FromStateId,
                        ToStateId = applied.ToStateId,
                        EventId = applied.EventId,
                        EventCode = applied.EventCode,
                        EventName = applied.EventName ?? string.Empty,
                        PrevStateMeta = new Dictionary<string, object>(),
                        PolicyId = pr.PolicyId,
                        PolicyHash = pr.PolicyHash ?? string.Empty,
                        PolicyJson = pr.PolicyJson ?? string.Empty
                    });
                }

                // Hooks (create hook rows in txn; dispatch after commit)
                var hookEmissions = await PolicyEnforcer.EmitHooksAsync(bp, instance, applied, load);
                for (var h = 0; h < hookEmissions.Count; h++) {
                    var he = hookEmissions[h];
                    var hookConsumers = await AckManager.GetHookConsumersAsync(bp.DefVersionId, instanceId, he.HookCode, ct);
                    var normHookConsumers = NormalizeConsumers(hookConsumers, _opt.DefaultConsumerId);

                    var hookAckGuid = string.Empty;
                    if (req.AckRequired) {
                        var hookAck = await AckManager.CreateHookAckAsync(he.HookId, normHookConsumers, (int)AckStatus.Pending, load);
                        hookAckGuid = hookAck.AckGuid ?? string.Empty;
                        hookAckGuids.Add(hookAckGuid);
                    }

                    for (var i = 0; i < normHookConsumers.Count; i++) {
                        var consumerId = normHookConsumers[i];
                        toDispatch.Add(new LifeCycleHookEvent {
                            ConsumerId = consumerId,
                            InstanceId = instanceId,
                            DefinitionVersionId = bp.DefVersionId,
                            ExternalRef = req.ExternalRef,
                            RequestId = req.RequestId,
                            OccurredAt = DateTimeOffset.UtcNow,
                            AckGuid = hookAckGuid,
                            AckRequired = req.AckRequired,
                            Payload = he.Payload as IReadOnlyDictionary<string, object?>,
                            HookId = he.HookId,
                            StateId = he.StateId,
                            OnEntry = he.OnEntry,
                            HookCode = he.HookCode ?? string.Empty,
                            OnSuccessEvent = he.OnSuccessEvent ?? string.Empty,
                            OnFailureEvent = he.OnFailureEvent ?? string.Empty,
                            NotBefore = he.NotBefore,
                            Deadline = he.Deadline
                        });
                    }
                }

                transaction.Commit();
                committed = true;

                // Dispatch AFTER commit (failures become notices; monitor will resend due to pending ACK rows).
                await DispatchEventsSafeAsync(toDispatch, ct);

                result.LifecycleAckGuids = lcAckGuids;
                result.HookAckGuids = hookAckGuids;
                return result;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                throw;
            } catch (Exception ex) {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                await RaiseNoticeSafeAsync(LifeCycleNotice.Error("TRIGGER_ERROR", ex.Message, ex), ct);
                throw;
            }
        }

        public async Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (consumerId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerId));
            if (string.IsNullOrWhiteSpace(ackGuid)) throw new ArgumentNullException(nameof(ackGuid));
            await AckManager.AckAsync(consumerId, ackGuid, outcome, message, retryAt, new DbExecutionLoad(ct));
        }

        public Task ClearCacheAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Clear(); return Task.CompletedTask; }

        public Task InvalidateAsync(int envCode, string defName, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Invalidate(envCode, defName); return Task.CompletedTask; }

        public Task InvalidateAsync(long defVersionId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Invalidate(defVersionId); return Task.CompletedTask; }

        public async Task RunMonitorOnceAsync(CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            // NOTE: stale-state / timeout-trigger monitor needs dedicated DAL queries; this run focuses on ACK resend.
            var nowUtc = DateTime.UtcNow;
            var pendingOlderThan = nowUtc.Subtract(_ackPendingResendAfter);
            var deliveredOlderThan = nowUtc.Subtract(_ackDeliveredResendAfter);

            for (var i = 0; i < _monitorConsumers.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var consumerId = _monitorConsumers[i];

                await ResendDispatchKindAsync(consumerId, (int)AckStatus.Pending, pendingOlderThan, ct);
                await ResendDispatchKindAsync(consumerId, (int)AckStatus.Delivered, deliveredOlderThan, ct);
            }
        }

        private async Task ResendDispatchKindAsync(long consumerId, int ackStatus, DateTime olderThanUtc, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            // Lifecycle
            var lc = await AckManager.ListPendingLifecycleDispatchAsync(consumerId, ackStatus, olderThanUtc, 0, _opt.MonitorPageSize, new DbExecutionLoad(ct));
            for (var i = 0; i < lc.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = lc[i];

                await RaiseNoticeSafeAsync(LifeCycleNotice.Warn("ACK_RETRY", $"kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceId}"), ct);
                await RaiseEventSafeAsync(item.Event, ct);
                await AckManager.MarkRetryAsync(item.AckId, item.ConsumerId, null, new DbExecutionLoad(ct));
            }

            // Hook
            var hk = await AckManager.ListPendingHookDispatchAsync(consumerId, ackStatus, olderThanUtc, 0, _opt.MonitorPageSize, new DbExecutionLoad(ct));
            for (var i = 0; i < hk.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = hk[i];

                await RaiseNoticeSafeAsync(LifeCycleNotice.Warn("ACK_RETRY", $"kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceId}"), ct);
                await RaiseEventSafeAsync(item.Event, ct);
                await AckManager.MarkRetryAsync(item.AckId, item.ConsumerId, null, new DbExecutionLoad(ct));
            }
        }

        private async Task DispatchEventsSafeAsync(IReadOnlyList<ILifeCycleEvent> events, CancellationToken ct) {
            for (var i = 0; i < events.Count; i++) { ct.ThrowIfCancellationRequested(); await RaiseEventSafeAsync(events[i], ct); }
        }

        private async Task RaiseEventSafeAsync(ILifeCycleEvent e, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            var h = EventRaised;
            if (h == null) return;
            try { await h.Invoke(e); } catch (Exception ex) { await RaiseNoticeSafeAsync(LifeCycleNotice.Error("EVENT_HANDLER_ERROR", ex.Message, ex), ct); }
        }

        private async Task RaiseNoticeSafeAsync(LifeCycleNotice n, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            var h = NoticeRaised;
            if (h == null) return;
            try { await h.Invoke(n); } catch { }
        }

        private static IReadOnlyList<long> NormalizeConsumers(IReadOnlyList<long>? consumers, long defaultConsumerId) {
            if (consumers == null || consumers.Count == 0) return new long[] { defaultConsumerId };
            var list = new List<long>(consumers.Count);
            for (var i = 0; i < consumers.Count; i++) { var c = consumers[i]; if (c > 0 && !list.Contains(c)) list.Add(c); }
            return list.Count == 0 ? new long[] { defaultConsumerId } : list;
        }
    }


}