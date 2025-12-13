using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;

namespace Haley.Services {

    public sealed class LifeCycleStateMonitor : IDisposable {

        private readonly ILifeCycleStateMachine _sm;
        private readonly ILifeCycleStateRepository _repo;
        private readonly LifeCycleMonitorOptions _options;
        private readonly Func<AckWorkItem, Task> _ackHandler;
        private readonly Func<AckWorkItem, Task>? _failedAckHandler;

        private Timer? _timer;
        private int _running;

        public DateTime? LastRunUtc { get; private set; }
        public Exception? LastError { get; private set; }
        public bool IsStarted => _timer != null;

        public LifeCycleStateMonitor(
            ILifeCycleStateMachine sm,
            ILifeCycleStateRepository repo,
            LifeCycleMonitorOptions? options,
            Func<AckWorkItem, Task> ackHandler,
            Func<AckWorkItem, Task>? failedAckHandler = null) {

            _sm = sm ?? throw new ArgumentNullException(nameof(sm));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _options = options ?? new LifeCycleMonitorOptions();
            _ackHandler = ackHandler ?? throw new ArgumentNullException(nameof(ackHandler));
            _failedAckHandler = failedAckHandler;
        }

        public void Start() {
            if (_timer != null) return;
            var period = TimeSpan.FromSeconds(_options.PollIntervalSeconds <= 0 ? 300 : _options.PollIntervalSeconds);
            _timer = new Timer(_ => _ = OnTickAsync(), null, TimeSpan.Zero, period);
        }

        public void Stop() {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            _timer = null;
        }

        public Task TickNowAsync() => OnTickAsync();

        private async Task OnTickAsync() {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;

            try {
                LastError = null;
                await ProcessTimeoutsAsync();
                await ProcessDueAcksAsync();

                if (_failedAckHandler != null)
                    await ProcessFailedAcksAsync();

                LastRunUtc = DateTime.UtcNow;
            } catch (Exception ex) {
                LastError = ex;
                if (_repo.ThrowExceptions) throw;
            } finally {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private async Task ProcessTimeoutsAsync() {
            var fb = await _repo.GetInstancesWithExpiredTimeouts(_options.TimeoutBatchSize);
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return;

            foreach (var row in fb.Result) {
                try {
                    var defVersion = row.GetInt("def_version");
                    var externalRef = row.GetString("external_ref") ?? string.Empty;
                    var eventCode = row.GetInt("event_code");

                    if (defVersion <= 0 || string.IsNullOrWhiteSpace(externalRef) || eventCode <= 0) continue;

                    var instanceKey = LifeCycleKeys.Instance(defVersion, externalRef);
                    await _sm.TriggerAsync(instanceKey, eventCode, "system_timeout", "Timeout event", null);
                } catch {
                    if (_repo.ThrowExceptions) throw;
                }
            }
        }

        private async Task ProcessDueAcksAsync() {
            var skip = 0;
            var limit = _options.AckBatchSize <= 0 ? 200 : _options.AckBatchSize;

            while (true) {
                var fb = await _repo.GetAck(LifeCycleAckFetchMode.DueForRetry, _options.AckMaxRetry, _options.AckRetryAfterMinutes, skip, limit);
                if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) break;

                foreach (var ackRow in fb.Result) {
                    await ProcessOneAckAsync(ackRow, _options.AckMaxRetry);
                }

                if (fb.Result.Count < limit) break;
                skip += fb.Result.Count;
            }
        }

        private async Task ProcessFailedAcksAsync() {
            var skip = 0;
            var limit = _options.AckBatchSize <= 0 ? 200 : _options.AckBatchSize;

            while (true) {
                var fb = await _repo.GetAck(LifeCycleAckFetchMode.Failed, _options.AckMaxRetry, _options.AckRetryAfterMinutes, skip, limit);
                if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) break;

                foreach (var ackRow in fb.Result) {
                    var work = await BuildWorkItemAsync(ackRow);
                    if (work == null) continue;

                    try {
                        await _failedAckHandler!(work);
                    } catch {
                        if (_repo.ThrowExceptions) throw;
                    }
                }

                if (fb.Result.Count < limit) break;
                skip += fb.Result.Count;
            }
        }

        private async Task ProcessOneAckAsync(Dictionary<string, object> ackRow, int maxRetry) {
            var ackId = ackRow.GetLong("id");
            var transitionLogId = ackRow.GetLong("transition_log");
            var messageId = ackRow.GetString("message_id") ?? string.Empty;
            var consumer = ackRow.GetInt("consumer");
            var retryCount = ackRow.GetInt("retry_count");

            if (ackId <= 0 || transitionLogId <= 0) return;

            // bump first (prevents tight re-pick by another monitor instance)
            var bump = await _repo.RetryAck(ackId);
            if (bump == null || !bump.Status) return;

            var work = await BuildWorkItemAsync(ackRow);
            if (work == null) return;

            try {
                await _sm.MarkAck(messageId, LifeCycleAckStatus.Delivered);
                await _ackHandler(work);
                await _sm.MarkAck(messageId, LifeCycleAckStatus.Processed);
            } catch {
                // Only mark permanently failed when retries are exhausted
                if (retryCount + 1 >= maxRetry)
                    await _sm.MarkAck(messageId, LifeCycleAckStatus.Failed);

                if (_repo.ThrowExceptions) throw;
            }
        }

        private async Task<AckWorkItem?> BuildWorkItemAsync(Dictionary<string, object> ackRow) {
            var ackId = ackRow.GetLong("id");
            var transitionLogId = ackRow.GetLong("transition_log");
            var messageId = ackRow.GetString("message_id") ?? string.Empty;

            var logFb = await _repo.GetTransitionLog(new LifeCycleKey(LifeCycleKeyType.Id, transitionLogId));
            if (logFb == null || !logFb.Status || logFb.Result == null) return null;
            var logRow = logFb.Result;

            var instanceId = logRow.GetLong("instance_id");
            var fromStateId = logRow.GetInt("from_state");
            var toStateId = logRow.GetInt("to_state");
            var eventId = logRow.GetInt("event");
            var actor = logRow.GetString("actor") ?? string.Empty;
            var metadataJson = logRow.GetString("metadata") ?? string.Empty;

            var instFb = await _repo.Get(LifeCycleEntity.Instance, new LifeCycleKey(LifeCycleKeyType.Id, instanceId));
            if (instFb == null || !instFb.Status || instFb.Result == null) return null;
            var instRow = instFb.Result;

            var defVersion = instRow.GetInt("def_version");
            var externalRef = instRow.GetString("external_ref") ?? string.Empty;

            var evFb = await _repo.Get(LifeCycleEntity.Event, new LifeCycleKey(LifeCycleKeyType.Id, eventId));
            if (evFb == null || !evFb.Status || evFb.Result == null) return null;
            var evRow = evFb.Result;

            var eventCode = evRow.GetInt("code");
            if (eventCode == 0) eventCode = eventId;
            var eventName = evRow.GetString("display_name") ?? evRow.GetString("name") ?? eventCode.ToString();

            return new AckWorkItem {
                AckId = ackId,
                MessageId = messageId,
                TransitionLogId = transitionLogId,
                InstanceId = instanceId,
                DefinitionVersion = defVersion,
                ExternalRef = externalRef,
                FromStateId = fromStateId,
                ToStateId = toStateId,
                EventId = eventId,
                EventCode = eventCode,
                EventName = eventName,
                Actor = actor,
                MetadataJson = metadataJson
            };
        }

        public void Dispose() => Stop();
    }
}
