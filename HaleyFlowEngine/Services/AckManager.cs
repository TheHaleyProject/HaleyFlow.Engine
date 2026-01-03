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
    internal sealed class DefaultAckManager : IAckManager {
        private readonly IWorkFlowDAL _dal;

        public DefaultAckManager(IWorkFlowDAL dal) { _dal = dal; }

        public async Task<ILifeCycleAckRef> CreateLifecycleAckAsync(long lifecycleId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default) {
            var ack = await _dal.Ack.InsertReturnRowAsync(load);
            if (ack == null) throw new InvalidOperationException("Ack insert failed.");

            var ackId = ack.GetLong("id");
            var ackGuid = ack.GetString("guid");

            await _dal.LcAck.AttachAsync(ackId, lifecycleId, load);
            for (int i = 0; i < consumerIds.Count; i++) await _dal.AckConsumer.UpsertByAckIdAndConsumerReturnIdAsync(ackId, consumerIds[i], initialAckStatus, load);

            return new LifeCycleAckRef { AckId = ackId, AckGuid = ackGuid };
        }

        public async Task<ILifeCycleAckRef> CreateHookAckAsync(long hookId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default) {
            var ack = await _dal.Ack.InsertReturnRowAsync(load);
            if (ack == null) throw new InvalidOperationException("Ack insert failed.");

            var ackId = ack.GetLong("id");
            var ackGuid = ack.GetString("guid");

            await _dal.HookAck.AttachAsync(ackId, hookId, load);
            for (int i = 0; i < consumerIds.Count; i++) await _dal.AckConsumer.UpsertByAckIdAndConsumerReturnIdAsync(ackId, consumerIds[i], initialAckStatus, load);

            return new LifeCycleAckRef { AckId = ackId, AckGuid = ackGuid };
        }

        public async Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string message = null, DateTimeOffset? retryAt = null, DbExecutionLoad load = default) {
            var status = outcome == AckOutcome.Delivered ? AckStatus.Delivered :
                         outcome == AckOutcome.Processed ? AckStatus.Processed :
                         outcome == AckOutcome.Failed ? AckStatus.Failed :
                         AckStatus.Pending;

            await _dal.AckConsumer.SetStatusByGuidAsync(ackGuid, consumerId, status, load);

            if (outcome == AckOutcome.Retry) {
                var ackId = await _dal.Ack.GetIdByGuidAsync(ackGuid, load);
                if (ackId.HasValue) await _dal.AckConsumer.MarkRetryAsync(ackId.Value, consumerId, load);
            }

            _ = message; _ = retryAt; // message/retryAt are not persisted in current schema
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
                var evt = new LifeCycleTransitionEvent {
                    ConsumerId = r.GetLong("consumer"),
                    InstanceId = r.GetLong("instance_id"),
                    DefinitionVersionId = 0, // not in dispatch query; if needed, extend query to join instance.def_version
                    ExternalRef = r.GetString("external_ref"),
                    RequestId = null,
                    OccurredAt = r.GetDateTimeOffset("lc_created") ?? DateTimeOffset.UtcNow,
                    AckGuid = r.GetString("ack_guid"),
                    AckRequired = true,
                    Payload = BuildLcPayload(r),
                    LifeCycleId = r.GetLong("lc_id"),
                    FromStateId = r.GetLong("from_state"),
                    ToStateId = r.GetLong("to_state"),
                    EventId = r.GetLong("event"),
                    EventCode = 0,
                    EventName = null,
                    PrevStateMeta = null,
                    PolicyId = null,
                    PolicyHash = null,
                    PolicyJson = null
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
                var evt = new LifeCycleHookEvent {
                    ConsumerId = r.GetLong("consumer"),
                    InstanceId = r.GetLong("instance_id"),
                    DefinitionVersionId = 0, // not in dispatch query; if needed, extend query to join instance.def_version
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

        private static IReadOnlyDictionary<string, object> BuildLcPayload(DbRow r) {
            var payload = new Dictionary<string, object>();
            var actor = r.GetString("actor");
            var body = r.GetString("payload");
            if (!string.IsNullOrWhiteSpace(actor)) payload["actor"] = actor;
            if (!string.IsNullOrWhiteSpace(body)) payload["payloadJson"] = body;
            return payload.Count == 0 ? null : payload;
        }
    }
}