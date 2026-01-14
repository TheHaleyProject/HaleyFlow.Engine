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

        private readonly Func<LifeCycleConsumerType, long?, CancellationToken, Task<IReadOnlyList<long>>> _consumers;

        // scheduling policy
        private readonly TimeSpan _pendingNextDue;    // T + 40s
        private readonly TimeSpan _deliveredNextDue;  // T + 4m

        public AckManager(
            IWorkFlowDAL dal,
            Func<LifeCycleConsumerType, long?, CancellationToken, Task<IReadOnlyList<long>>> consumers = null,
            TimeSpan? pendingNextDue = null,
            TimeSpan? deliveredNextDue = null) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _consumers = consumers ?? ((dv, iid, ct) => Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>()));

            _pendingNextDue = pendingNextDue ?? TimeSpan.FromSeconds(40);
            _deliveredNextDue = deliveredNextDue ?? TimeSpan.FromMinutes(4);
        }

        public Task<IReadOnlyList<long>> GetTransitionConsumersAsync(long defVersionId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _consumers(LifeCycleConsumerType.Transition, defVersionId, ct); }

        public Task<IReadOnlyList<long>> GetHookConsumersAsync(long defVersionId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _consumers(LifeCycleConsumerType.Hook, defVersionId, ct); }

        public async Task<ILifeCycleAckRef> CreateLifecycleAckAsync(long lifecycleId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (lifecycleId <= 0) throw new ArgumentOutOfRangeException(nameof(lifecycleId));

            var existingAckId = await _dal.LcAck.GetAckIdByLcIdAsync(lifecycleId, load);
            if (existingAckId.HasValue && existingAckId.Value > 0) {
                // IMPORTANT: do NOT reschedule existing consumers; only insert missing ones.
                await EnsureConsumersInsertOnlyAsync(existingAckId.Value, consumerIds, initialAckStatus, load);
                return await GetAckRefByIdAsync(existingAckId.Value, load);
            }

            var ack = await _dal.Ack.InsertReturnRowAsync(load);
            if (ack == null) throw new InvalidOperationException("Ack insert failed.");

            var ackId = ack.GetLong("id");
            var ackGuid = ack.GetString("guid");
            if (ackId <= 0 || string.IsNullOrWhiteSpace(ackGuid)) throw new InvalidOperationException("Ack insert failed (id/guid missing).");

            await _dal.LcAck.AttachAsync(ackId, lifecycleId, load);
            await EnsureConsumersInsertOnlyAsync(ackId, consumerIds, initialAckStatus, load);

            return new LifeCycleAckRef { AckId = ackId, AckGuid = ackGuid! };
        }

        public async Task<ILifeCycleAckRef> CreateHookAckAsync(long hookId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (hookId <= 0) throw new ArgumentOutOfRangeException(nameof(hookId));

            var existingAckId = await _dal.HookAck.GetAckIdByHookIdAsync(hookId, load);
            if (existingAckId.HasValue && existingAckId.Value > 0) {
                // IMPORTANT: do NOT reschedule existing consumers; only insert missing ones.
                await EnsureConsumersInsertOnlyAsync(existingAckId.Value, consumerIds, initialAckStatus, load);
                return await GetAckRefByIdAsync(existingAckId.Value, load);
            }

            var ack = await _dal.Ack.InsertReturnRowAsync(load);
            if (ack == null) throw new InvalidOperationException("Ack insert failed.");

            var ackId = ack.GetLong("id");
            var ackGuid = ack.GetString("guid");
            if (ackId <= 0 || string.IsNullOrWhiteSpace(ackGuid)) throw new InvalidOperationException("Ack insert failed (id/guid missing).");

            await _dal.HookAck.AttachAsync(ackId, hookId, load);
            await EnsureConsumersInsertOnlyAsync(ackId, consumerIds, initialAckStatus, load);

            return new LifeCycleAckRef { AckId = ackId, AckGuid = ackGuid! };
        }

        // ACK FROM CONSUMER
        public async Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (consumerId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerId));
            if (string.IsNullOrWhiteSpace(ackGuid)) throw new ArgumentNullException(nameof(ackGuid));

            // Decide status + due scheduling
            var (status, nextDueUtc) = ComputeOutcomeStatusAndDue(outcome, retryAt);

            // Single DB call; no ackId fetch needed.
            var affected = await _dal.AckConsumer.SetStatusAndDueByGuidAsync(ackGuid, consumerId, (int)status, nextDueUtc, load);
            if (affected <= 0) throw new InvalidOperationException($"AckConsumer not found. guid={ackGuid}, consumer={consumerId}");

            _ = message;
        }

        public async Task MarkRetryAsync(long ackId, long consumerId, DateTimeOffset? retryAt = null, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (ackId <= 0) throw new ArgumentOutOfRangeException(nameof(ackId));
            if (consumerId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerId));

            var nextDueUtc = (retryAt?.UtcDateTime) ?? (DateTime.UtcNow + _pendingNextDue);
            await _dal.AckConsumer.SetStatusAndDueAsync(ackId, consumerId, (int)AckStatus.Pending, nextDueUtc, load);
        }

        public Task SetStatusAsync(long ackId, long consumerId, int ackStatus, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();

            // NOTE: status-only is dangerous now; caller must decide due semantics.
            // Keep it for compatibility but do NOT touch next_due (pass NULL only when moving to terminal states).
            DateTime? nextDueUtc = null;
            if (ackStatus == (int)AckStatus.Pending) nextDueUtc = DateTime.UtcNow + _pendingNextDue;
            else if (ackStatus == (int)AckStatus.Delivered) nextDueUtc = DateTime.UtcNow + _deliveredNextDue;
            else nextDueUtc = null;

            return _dal.AckConsumer.SetStatusAndDueAsync(ackId, consumerId, ackStatus, nextDueUtc, load);
        }

        // DISPATCH LISTING (monitor uses these)
        public async Task<IReadOnlyList<ILifeCycleDispatchItem>> ListDueLifecycleDispatchAsync(long consumerId, int ackStatus, int ttlSeconds, int skip, int take, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            var rows = await _dal.AckDispatch.ListDueLifecyclePagedAsync(consumerId, ackStatus, ttlSeconds, skip, take, load);
            var list = new List<ILifeCycleDispatchItem>(rows.Count);
            foreach (var r in rows) {
                load.Ct.ThrowIfCancellationRequested();
                var evt = new LifeCycleTransitionEvent {
                    DefinitionVersionId = r.GetLong("def_version_id"),
                    ConsumerId = r.GetLong("consumer"),
                    InstanceGuid = r.GetString("instance_guid"),
                    ExternalRef = r.GetString("external_ref") ?? string.Empty,
                    RequestId = null,
                    OccurredAt = r.GetDateTimeOffset("lc_created") ?? DateTimeOffset.UtcNow,
                    AckGuid = r.GetString("ack_guid") ?? string.Empty,
                    AckRequired = true,
                    Payload = null,
                    LifeCycleId = r.GetLong("lc_id"),
                    FromStateId = r.GetLong("from_state"),
                    ToStateId = r.GetLong("to_state"),
                    EventCode = r.GetNullableInt("event_code") ?? 0,
                    EventName = r.GetString("event_name") ?? string.Empty,
                    PrevStateMeta = null,
                    PolicyJson = r.GetString("policy_json")
                };
                list.Add(new LifeCycleDispatchItem {
                    Kind = LifeCycleEventKind.Transition,
                    AckId = r.GetLong("ack_id"),
                    AckGuid = r.GetString("ack_guid") ?? string.Empty,
                    ConsumerId = r.GetLong("consumer"),
                    AckStatus = r.GetInt("status"),
                    TriggerCount = r.GetInt("trigger_count"),
                    LastTrigger = r.GetDateTime("last_trigger") ?? DateTime.UtcNow,
                    NextDue = r.GetDateTime("next_due"),
                    Event = evt
                });
            }
            return list;
        }

        public async Task<IReadOnlyList<ILifeCycleDispatchItem>> ListDueHookDispatchAsync(long consumerId, int ackStatus, int ttlSeconds, int skip, int take, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            var rows = await _dal.AckDispatch.ListDueHookPagedAsync(consumerId, ackStatus, ttlSeconds, skip, take, load);
            var list = new List<ILifeCycleDispatchItem>(rows.Count);
            foreach (var r in rows) {
                load.Ct.ThrowIfCancellationRequested();
                var evt = new LifeCycleHookEvent {
                    ConsumerId = r.GetLong("consumer"),
                    InstanceGuid = r.GetString("instance_guid"),
                    DefinitionVersionId = r.GetNullableLong("def_version_id") ?? 0,
                    ExternalRef = r.GetString("external_ref") ?? string.Empty,
                    RequestId = null,
                    OccurredAt = r.GetDateTimeOffset("hook_created") ?? DateTimeOffset.UtcNow,
                    AckGuid = r.GetString("ack_guid") ?? string.Empty,
                    AckRequired = true,
                    Payload = null,
                    HookId = r.GetLong("hook_id"),
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
                    TriggerCount = r.GetInt("trigger_count"),
                    LastTrigger = r.GetDateTime("last_trigger") ?? DateTime.UtcNow,
                    NextDue = r.GetDateTime("next_due"),
                    Event = evt
                });
            }
            return list;
        }

        public async Task<int> CountDueLifecycleDispatchAsync(int ackStatus, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            return (await _dal.AckDispatch.CountDueLifecycleAsync(ackStatus, load)) ?? 0;
        }

        public async Task<int> CountDueHookDispatchAsync(int ackStatus, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            return (await _dal.AckDispatch.CountDueHookAsync(ackStatus, load)) ?? 0;
        }

        private (AckStatus status, DateTime? nextDueUtc) ComputeOutcomeStatusAndDue(AckOutcome outcome, DateTimeOffset? retryAt) {
            // Use next_due to drive monitor. Terminal states => next_due NULL.
            // Pending/Delivered => schedule next_due (either retryAt or defaults).
            if (outcome == AckOutcome.Delivered)
                return (AckStatus.Delivered, DateTime.UtcNow + _deliveredNextDue);

            if (outcome == AckOutcome.Processed)
                return (AckStatus.Processed, null);

            if (outcome == AckOutcome.Failed)
                return (AckStatus.Failed, null);

            if (outcome == AckOutcome.Retry)
                return (AckStatus.Pending, retryAt?.UtcDateTime ?? (DateTime.UtcNow + _pendingNextDue));

            return (AckStatus.Pending, DateTime.UtcNow + _pendingNextDue);
        }

        private async Task EnsureConsumersInsertOnlyAsync(long ackId, IReadOnlyList<long> consumerIds, int initialAckStatus, DbExecutionLoad load) {
            load.Ct.ThrowIfCancellationRequested();
            var ids = NormalizeConsumers(consumerIds);
            if (ids.Count == 0) return;

            // Only INSERT missing rows. Do not overwrite existing status/next_due.
            // (This prevents rescheduling acks on every startup or re-attach call.)
            for (var i = 0; i < ids.Count; i++) {
                load.Ct.ThrowIfCancellationRequested();
                var consumerId = ids[i];

                var existing = await _dal.AckConsumer.GetByKeyAsync(ackId, consumerId, load);
                if (existing != null) continue;

                var nextDueUtc = ComputeInitialNextDueUtc(initialAckStatus);
                await _dal.AckConsumer.UpsertByAckIdAndConsumerReturnIdAsync(ackId, consumerId, initialAckStatus, nextDueUtc, load);
            }
        }

        private DateTime? ComputeInitialNextDueUtc(int ackStatus) {
            if (ackStatus == (int)AckStatus.Pending) return DateTime.UtcNow + _pendingNextDue;
            if (ackStatus == (int)AckStatus.Delivered) return DateTime.UtcNow + _deliveredNextDue;
            return null; // Processed/Failed etc.
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