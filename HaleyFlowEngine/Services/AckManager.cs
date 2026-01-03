using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    internal sealed class AckManager : IAckManager {
        private readonly IWorkFlowDAL _dal;
        private readonly Func<long, long, CancellationToken, Task<IReadOnlyList<long>>> _transitionConsumers;
        private readonly Func<long, long, string, CancellationToken, Task<IReadOnlyList<long>>> _hookConsumers;

        public AckManager(IWorkFlowDAL dal, Func<long, long, CancellationToken, Task<IReadOnlyList<long>>>? transitionConsumers = null, Func<long, long, string, CancellationToken, Task<IReadOnlyList<long>>>? hookConsumers = null) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _transitionConsumers = transitionConsumers ?? ((dv, iid, ct) => Task.FromResult<IReadOnlyList<long>>(new long[] { 0 }));
            _hookConsumers = hookConsumers ?? ((dv, iid, code, ct) => Task.FromResult<IReadOnlyList<long>>(new long[] { 0 }));
        }

        public Task<IReadOnlyList<long>> GetTransitionConsumersAsync(long defVersionId, long instanceId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _transitionConsumers(defVersionId, instanceId, ct); }

        public Task<IReadOnlyList<long>> GetHookConsumersAsync(long defVersionId, long instanceId, string hookCode, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _hookConsumers(defVersionId, instanceId, hookCode, ct); }

        public async Task<ILifeCycleAckRef> CreateLifecycleAckAsync(long lifecycleId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default) {
            var ack = await _dal.Ack.InsertReturnRowAsync(load);
            if (ack == null) throw new InvalidOperationException("Ack insert failed.");

            var ackId = ack.GetLong("id");
            var ackGuid = ack.GetString("guid");

            await _dal.LcAck.AttachAsync(ackId, lifecycleId, load);

            for (var i = 0; i < consumerIds.Count; i++) {
                await _dal.AckConsumer.UpsertByAckIdAndConsumerReturnIdAsync(ackId, consumerIds[i], initialAckStatus, load);
            }

            return new LifeCycleAckRef { AckId = ackId, AckGuid = ackGuid };
        }

        public async Task<ILifeCycleAckRef> CreateHookAckAsync(long hookId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default) {
            var ack = await _dal.Ack.InsertReturnRowAsync(load);
            if (ack == null) throw new InvalidOperationException("Ack insert failed.");

            var ackId = ack.GetLong("id");
            var ackGuid = ack.GetString("guid");

            await _dal.HookAck.AttachAsync(ackId, hookId, load);

            for (var i = 0; i < consumerIds.Count; i++) {
                await _dal.AckConsumer.UpsertByAckIdAndConsumerReturnIdAsync(ackId, consumerIds[i], initialAckStatus, load);
            }

            return new LifeCycleAckRef { AckId = ackId, AckGuid = ackGuid };
        }

        public async Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, DbExecutionLoad load = default) {
            var status = outcome == AckOutcome.Delivered ? AckStatus.Delivered :
                         outcome == AckOutcome.Processed ? AckStatus.Processed :
                         outcome == AckOutcome.Failed ? AckStatus.Failed :
                         AckStatus.Pending;

            await _dal.AckConsumer.SetStatusByGuidAsync(ackGuid, consumerId, (int)status, load);

            if (outcome == AckOutcome.Retry) {
                var ackId = await _dal.Ack.GetIdByGuidAsync(ackGuid, load);
                if (ackId.HasValue) await _dal.AckConsumer.MarkRetryAsync(ackId.Value, consumerId, load);
            }

            _ = message; _ = retryAt;
        }

        public async Task MarkRetryAsync(long ackId, long consumerId, DateTimeOffset? retryAt = null, DbExecutionLoad load = default) {
            await _dal.AckConsumer.MarkRetryAsync(ackId, consumerId, load);
            _ = retryAt;
        }

        public Task SetStatusAsync(long ackId, long consumerId, int ackStatus, DbExecutionLoad load = default) { return _dal.AckConsumer.SetStatusAsync(ackId, consumerId, ackStatus, load); }

        public async Task<IReadOnlyList<ILifeCycleDispatchItem>> ListPendingLifecycleDispatchAsync(long consumerId, int ackStatus, DateTime utcOlderThan, int skip, int take, DbExecutionLoad load = default) {
            var rows = await _dal.AckDispatch.ListPendingLifecycleReadyPagedAsync(consumerId, ackStatus, utcOlderThan, skip, take, load);
            var list = new List<ILifeCycleDispatchItem>(rows.Count);

            foreach (var r in rows) {
                // Columns expected from your ack_dispatch lifecycle query (adjust keys only if your select differs):
                // ack_id, ack_guid, consumer, status, retry_count, last_retry, instance_id, external_ref, def_version_id, lc_id, from_state, to_state, event_id, event_code, event_name
                var evt = new LifeCycleTransitionEvent {
                    ConsumerId = r.GetLong("consumer"),
                    InstanceId = r.GetLong("instance_id"),
                    DefinitionVersionId = r.GetNullableLong("def_version_id") ?? 0,
                    ExternalRef = r.GetString("external_ref"),
                    RequestId = null,
                    OccurredAt = r.GetDateTimeOffset("lc_created") ?? DateTimeOffset.UtcNow,
                    AckGuid = r.GetString("ack_guid"),
                    AckRequired = true,
                    Payload = null,
                    LifeCycleId = r.GetLong("lc_id"),
                    FromStateId = r.GetLong("from_state"),
                    ToStateId = r.GetLong("to_state"),
                    EventId = r.GetLong("event_id"),
                    EventCode = r.GetNullableInt("event_code") ?? 0,
                    EventName = r.GetString("event_name") ?? string.Empty,
                    PrevStateMeta = null,
                    PolicyId = r.GetNullableLong("policy_id"),
                    PolicyHash = r.GetString("policy_hash"),
                    PolicyJson = r.GetString("policy_json")
                };

                list.Add(new LifeCycleDispatchItem {
                    Kind = LifeCycleDispatchKind.Lifecycle,
                    AckId = r.GetLong("ack_id"),
                    AckGuid = r.GetString("ack_guid"),
                    ConsumerId = r.GetLong("consumer"),
                    AckStatus = r.GetInt("status"),
                    RetryCount = r.GetInt("retry_count"),
                    LastRetryUtc = (r.GetDateTime("last_retry") ?? DateTime.UtcNow),
                    Event = evt
                });
            }

            return list;
        }

        public async Task<IReadOnlyList<ILifeCycleDispatchItem>> ListPendingHookDispatchAsync(long consumerId, int ackStatus, DateTime utcOlderThan, int skip, int take, DbExecutionLoad load = default) {
            var rows = await _dal.AckDispatch.ListPendingHookReadyPagedAsync(consumerId, ackStatus, utcOlderThan, skip, take, load);
            var list = new List<ILifeCycleDispatchItem>(rows.Count);

            foreach (var r in rows) {
                // Columns expected from your ack_dispatch hook query (adjust keys only if your select differs):
                // ack_id, ack_guid, consumer, status, retry_count, last_retry, instance_id, external_ref, def_version_id, hook_id, state_id, on_entry, route, via_event, hook_created
                var evt = new LifeCycleHookEvent {
                    ConsumerId = r.GetLong("consumer"),
                    InstanceId = r.GetLong("instance_id"),
                    DefinitionVersionId = r.GetNullableLong("def_version_id") ?? 0,
                    ExternalRef = r.GetString("external_ref"),
                    RequestId = null,
                    OccurredAt = r.GetDateTimeOffset("hook_created") ?? DateTimeOffset.UtcNow,
                    AckGuid = r.GetString("ack_guid"),
                    AckRequired = true,
                    Payload = null,
                    HookId = r.GetLong("hook_id"),
                    StateId = r.GetLong("state_id"),
                    OnEntry = r.GetBool("on_entry"),
                    HookCode = r.GetString("route"),
                    OnSuccessEvent = null,
                    OnFailureEvent = null,
                    NotBefore = null,
                    Deadline = null
                };

                list.Add(new LifeCycleDispatchItem {
                    Kind = LifeCycleDispatchKind.Hook,
                    AckId = r.GetLong("ack_id"),
                    AckGuid = r.GetString("ack_guid"),
                    ConsumerId = r.GetLong("consumer"),
                    AckStatus = r.GetInt("status"),
                    RetryCount = r.GetInt("retry_count"),
                    LastRetryUtc = (r.GetDateTime("last_retry") ?? DateTime.UtcNow),
                    Event = evt
                });
            }

            return list;
        }

        public async Task<int> CountPendingLifecycleDispatchAsync(int ackStatus, DateTime utcOlderThan, DbExecutionLoad load = default) { return (await _dal.AckDispatch.CountPendingLifecycleReadyAsync(ackStatus, utcOlderThan, load)) ?? 0; }

        public async Task<int> CountPendingHookDispatchAsync(int ackStatus, DateTime utcOlderThan, DbExecutionLoad load = default) { return (await _dal.AckDispatch.CountPendingHookReadyAsync(ackStatus, utcOlderThan, load)) ?? 0; }
    }
}