using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
            _transitionConsumers = transitionConsumers ?? ((dv, iid, ct) => Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>()));
            _hookConsumers = hookConsumers ?? ((dv, iid, code, ct) => Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>()));
        }

        public Task<IReadOnlyList<long>> GetTransitionConsumersAsync(long defVersionId, long instanceId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _transitionConsumers(defVersionId, instanceId, ct); }

        public Task<IReadOnlyList<long>> GetHookConsumersAsync(long defVersionId, long instanceId, string hookCode, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _hookConsumers(defVersionId, instanceId, hookCode, ct); }

        public async Task<ILifeCycleAckRef> CreateLifecycleAckAsync(long lifecycleId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (lifecycleId <= 0) throw new ArgumentOutOfRangeException(nameof(lifecycleId));

            var existingAckId = await _dal.LcAck.GetAckIdByLcIdAsync(lifecycleId, load);
            if (existingAckId.HasValue && existingAckId.Value > 0) {
                await EnsureConsumersAsync(existingAckId.Value, consumerIds, initialAckStatus, load);
                return await GetAckRefByIdAsync(existingAckId.Value, load);
            }

            var ack = await _dal.Ack.InsertReturnRowAsync(load);
            if (ack == null) throw new InvalidOperationException("Ack insert failed.");

            var ackId = ack.GetLong("id");
            var ackGuid = ack.GetString("guid");
            if (ackId <= 0 || string.IsNullOrWhiteSpace(ackGuid)) throw new InvalidOperationException("Ack insert failed (id/guid missing).");

            await _dal.LcAck.AttachAsync(ackId, lifecycleId, load);
            await EnsureConsumersAsync(ackId, consumerIds, initialAckStatus, load);

            return new LifeCycleAckRef { AckId = ackId, AckGuid = ackGuid! };
        }

        public async Task<ILifeCycleAckRef> CreateHookAckAsync(long hookId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (hookId <= 0) throw new ArgumentOutOfRangeException(nameof(hookId));

            var existingAckId = await _dal.HookAck.GetAckIdByHookIdAsync(hookId, load);
            if (existingAckId.HasValue && existingAckId.Value > 0) {
                await EnsureConsumersAsync(existingAckId.Value, consumerIds, initialAckStatus, load);
                return await GetAckRefByIdAsync(existingAckId.Value, load);
            }

            var ack = await _dal.Ack.InsertReturnRowAsync(load);
            if (ack == null) throw new InvalidOperationException("Ack insert failed.");

            var ackId = ack.GetLong("id");
            var ackGuid = ack.GetString("guid");
            if (ackId <= 0 || string.IsNullOrWhiteSpace(ackGuid)) throw new InvalidOperationException("Ack insert failed (id/guid missing).");

            await _dal.HookAck.AttachAsync(ackId, hookId, load);
            await EnsureConsumersAsync(ackId, consumerIds, initialAckStatus, load);

            return new LifeCycleAckRef { AckId = ackId, AckGuid = ackGuid! };
        }

        public async Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (consumerId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerId));
            if (string.IsNullOrWhiteSpace(ackGuid)) throw new ArgumentNullException(nameof(ackGuid));

            var status = outcome == AckOutcome.Delivered ? AckStatus.Delivered :
                         outcome == AckOutcome.Processed ? AckStatus.Processed :
                         outcome == AckOutcome.Failed ? AckStatus.Failed :
                         AckStatus.Pending;

            var affected = await _dal.AckConsumer.SetStatusByGuidAsync(ackGuid, consumerId, (int)status, load);
            if (affected <= 0) throw new InvalidOperationException($"AckConsumer not found. guid={ackGuid}, consumer={consumerId}");

            if (outcome == AckOutcome.Retry) {
                var ackId = await _dal.Ack.GetIdByGuidAsync(ackGuid, load);
                if (!ackId.HasValue || ackId.Value <= 0) throw new InvalidOperationException($"Ack not found. guid={ackGuid}");
                await _dal.AckConsumer.SetStatusAsync(ackId.Value, consumerId, (int)AckStatus.Pending, load);
                await _dal.AckConsumer.MarkRetryAsync(ackId.Value, consumerId, load);
            }

            _ = message; _ = retryAt;
        }

        public async Task MarkRetryAsync(long ackId, long consumerId, DateTimeOffset? retryAt = null, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (ackId <= 0) throw new ArgumentOutOfRangeException(nameof(ackId));
            if (consumerId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerId));
            await _dal.AckConsumer.SetStatusAsync(ackId, consumerId, (int)AckStatus.Pending, load);
            await _dal.AckConsumer.MarkRetryAsync(ackId, consumerId, load);
            _ = retryAt;
        }

        public Task SetStatusAsync(long ackId, long consumerId, int ackStatus, DbExecutionLoad load = default) { load.Ct.ThrowIfCancellationRequested(); return _dal.AckConsumer.SetStatusAsync(ackId, consumerId, ackStatus, load); }

        public async Task<IReadOnlyList<ILifeCycleDispatchItem>> ListPendingLifecycleDispatchAsync(long consumerId, int ackStatus, DateTime utcOlderThan, int skip, int take, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            var rows = await _dal.AckDispatch.ListPendingLifecycleReadyPagedAsync(consumerId, ackStatus, utcOlderThan, skip, take, load);
            var list = new List<ILifeCycleDispatchItem>(rows.Count);

            foreach (var r in rows) {
                load.Ct.ThrowIfCancellationRequested();

                var evt = new LifeCycleTransitionEvent {
                    ConsumerId = r.GetLong("consumer"),
                    InstanceId = r.GetLong("instance_id"),
                    DefinitionVersionId = r.GetNullableLong("def_version_id") ?? 0,
                    ExternalRef = r.GetString("external_ref") ?? string.Empty,
                    RequestId = null,
                    OccurredAt = r.GetDateTimeOffset("lc_created") ?? DateTimeOffset.UtcNow,
                    AckGuid = r.GetString("ack_guid") ?? string.Empty,
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
                    Kind = LifeCycleEventKind.Transition,
                    AckId = r.GetLong("ack_id"),
                    AckGuid = r.GetString("ack_guid") ?? string.Empty,
                    ConsumerId = r.GetLong("consumer"),
                    AckStatus = r.GetInt("status"),
                    RetryCount = r.GetInt("retry_count"),
                    LastRetryUtc = r.GetDateTime("last_retry") ?? DateTime.UtcNow,
                    Event = evt
                });
            }

            return list;
        }

        public async Task<IReadOnlyList<ILifeCycleDispatchItem>> ListPendingHookDispatchAsync(long consumerId, int ackStatus, DateTime utcOlderThan, int skip, int take, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            var rows = await _dal.AckDispatch.ListPendingHookReadyPagedAsync(consumerId, ackStatus, utcOlderThan, skip, take, load);
            var list = new List<ILifeCycleDispatchItem>(rows.Count);

            foreach (var r in rows) {
                load.Ct.ThrowIfCancellationRequested();

                var evt = new LifeCycleHookEvent {
                    ConsumerId = r.GetLong("consumer"),
                    InstanceId = r.GetLong("instance_id"),
                    DefinitionVersionId = r.GetNullableLong("def_version_id") ?? 0,
                    ExternalRef = r.GetString("external_ref") ?? string.Empty,
                    RequestId = null,
                    OccurredAt = r.GetDateTimeOffset("hook_created") ?? DateTimeOffset.UtcNow,
                    AckGuid = r.GetString("ack_guid") ?? string.Empty,
                    AckRequired = true,
                    Payload = null,
                    HookId = r.GetLong("hook_id"),
                    StateId = r.GetLong("state_id"),
                    OnEntry = r.GetBool("on_entry"),
                    HookCode = r.GetString("route") ?? string.Empty,
                    OnSuccessEvent = null,
                    OnFailureEvent = null,
                    NotBefore = null,
                    Deadline = null
                };

                list.Add(new LifeCycleDispatchItem {
                    Kind = LifeCycleEventKind.Hook,
                    AckId = r.GetLong("ack_id"),
                    AckGuid = r.GetString("ack_guid") ?? string.Empty,
                    ConsumerId = r.GetLong("consumer"),
                    AckStatus = r.GetInt("status"),
                    RetryCount = r.GetInt("retry_count"),
                    LastRetryUtc = r.GetDateTime("last_retry") ?? DateTime.UtcNow,
                    Event = evt
                });
            }

            return list;
        }

        public async Task<int> CountPendingLifecycleDispatchAsync(int ackStatus, DateTime utcOlderThan, DbExecutionLoad load = default) { load.Ct.ThrowIfCancellationRequested(); return (await _dal.AckDispatch.CountPendingLifecycleReadyAsync(ackStatus, utcOlderThan, load)) ?? 0; }

        public async Task<int> CountPendingHookDispatchAsync(int ackStatus, DateTime utcOlderThan, DbExecutionLoad load = default) { load.Ct.ThrowIfCancellationRequested(); return (await _dal.AckDispatch.CountPendingHookReadyAsync(ackStatus, utcOlderThan, load)) ?? 0; }

        private async Task EnsureConsumersAsync(long ackId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load) {
            load.Ct.ThrowIfCancellationRequested();
            var ids = NormalizeConsumers(consumerIds);
            for (var i = 0; i < ids.Count; i++) {
                load.Ct.ThrowIfCancellationRequested();
                await _dal.AckConsumer.UpsertByAckIdAndConsumerReturnIdAsync(ackId, ids[i], initialAckStatus, load); // DAL does EXISTS->INSERT
            }
        }

        private async Task<ILifeCycleAckRef> GetAckRefByIdAsync(long ackId, DbExecutionLoad load) {
            load.Ct.ThrowIfCancellationRequested();
            var row = await _dal.Ack.GetByIdAsync(ackId, load);
            if (row == null) throw new InvalidOperationException($"Ack not found. id={ackId}");
            var guid = row.GetString("guid");
            if (string.IsNullOrWhiteSpace(guid)) throw new InvalidOperationException($"Ack guid missing. id={ackId}");
            return new LifeCycleAckRef { AckId = ackId, AckGuid = guid! };
        }

        private static IReadOnlyList<long> NormalizeConsumers(IReadOnlyList<long> consumerIds) {
            if (consumerIds == null || consumerIds.Count == 0) return Array.Empty<long>();
            var set = new HashSet<long>();
            for (var i = 0; i < consumerIds.Count; i++) if (consumerIds[i] > 0) set.Add(consumerIds[i]);
            if (set.Count == 0) return Array.Empty<long>();
            var arr = new long[set.Count];
            set.CopyTo(arr);
            return arr;
        }
    }

}