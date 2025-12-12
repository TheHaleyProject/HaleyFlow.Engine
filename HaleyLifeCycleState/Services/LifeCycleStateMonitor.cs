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

        private Timer? _timer;
        private int _running;

        public LifeCycleStateMonitor(ILifeCycleStateMachine sm, ILifeCycleStateRepository repo, LifeCycleMonitorOptions options, Func<AckWorkItem, Task> ackHandler) {
            _sm = sm ?? throw new ArgumentNullException(nameof(sm));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _options = options ?? new LifeCycleMonitorOptions();
            _ackHandler = ackHandler ?? throw new ArgumentNullException(nameof(ackHandler));
        }

        public void Start() {
            if (_timer != null) return;
            _timer = new Timer(async _ => await OnTickAsync().ConfigureAwait(false), null, TimeSpan.Zero, TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        }

        public void Stop() {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private async Task OnTickAsync() {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            try {
                await ProcessTimeoutsAsync().ConfigureAwait(false);
                await ProcessPendingAcksAsync().ConfigureAwait(false);
            } finally {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private async Task ProcessTimeoutsAsync() {
            var fb = await _repo.GetInstancesWithExpiredTimeouts(_options.TimeoutBatchSize).ConfigureAwait(false);
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return;

            foreach (var row in fb.Result) {
                try {
                    var defVersion = row.GetInt("def_version");
                    var externalRef = row.GetString("external_ref") ?? string.Empty;
                    var eventCode = row.GetInt("event_code");

                    if (defVersion <= 0 || string.IsNullOrWhiteSpace(externalRef) || eventCode <= 0) continue;

                    await _sm.TriggerAsync(defVersion, externalRef, eventCode, "system_timeout", "Timeout event", null).ConfigureAwait(false);
                } catch {
                    if (_repo.ThrowExceptions) throw;
                }
            }
        }

        private async Task ProcessPendingAcksAsync() {
            var fb = await _repo.Ack_GetDueForRetry(_options.AckMaxRetry, _options.AckRetryAfterMinutes).ConfigureAwait(false);
            if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return;

            foreach (var ackRow in fb.Result) {
                var ackId = ackRow.GetLong("id");
                var transitionLogId = ackRow.GetLong("transition_log");
                var messageId = ackRow.GetString("message_id") ?? string.Empty;

                try {
                    // Load transition_log + data + instance + event
                    var logFb = await _repo.GetLogById(transitionLogId).ConfigureAwait(false);
                    if (logFb == null || !logFb.Status || logFb.Result == null) continue;
                    var logRow = logFb.Result;

                    var instanceId = logRow.GetLong("instance_id");
                    var fromStateId = logRow.GetInt("from_state");
                    var toStateId = logRow.GetInt("to_state");
                    var eventId = logRow.GetInt("event");
                    var actor = logRow.GetString("actor") ?? string.Empty;
                    var metadataJson = logRow.GetString("metadata") ?? string.Empty;

                    var instFb = await _repo.GetInstanceById(instanceId).ConfigureAwait(false);
                    if (instFb == null || !instFb.Status || instFb.Result == null) continue;
                    var instRow = instFb.Result;
                    var defVersion = instRow.GetInt("def_version");
                    var externalRef = instRow.GetString("external_ref") ?? string.Empty;

                    var evFb = await _repo.GetEventsByVersion(defVersion).ConfigureAwait(false);
                    if (evFb == null || !evFb.Status || evFb.Result == null) continue;
                    var evRows = evFb.Result;
                    var evRow = evRows.Find(r => r.GetInt("id") == eventId);
                    if (evRow == null) continue;

                    var eventCode = evRow.GetInt("code");
                    if (eventCode == 0) eventCode = eventId;
                    var eventName = evRow.GetString("display_name") ?? evRow.GetString("name") ?? eventCode.ToString();

                    var work = new AckWorkItem {
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

                    await _sm.Ack_MarkByMessageAsync(messageId, LifeCycleAckStatus.Delivered).ConfigureAwait(false);
                    await _ackHandler(work).ConfigureAwait(false);
                    await _sm.Ack_MarkByMessageAsync(messageId, LifeCycleAckStatus.Processed).ConfigureAwait(false);
                } catch {
                    await _sm.Ack_MarkByMessageAsync(messageId, LifeCycleAckStatus.Failed).ConfigureAwait(false);
                    if (_repo.ThrowExceptions) throw;
                }
            }
        }


        public void Dispose() {
            Stop();
            _timer?.Dispose();
        }
    }
}
