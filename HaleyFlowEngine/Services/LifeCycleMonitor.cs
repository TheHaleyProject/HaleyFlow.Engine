using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;

namespace Haley.Services {
    // Monitor is readonly. It must never call TriggerAsync/MarkAck/RetryAck/etc. Its job is: detect + notify.
    public sealed class LifeCycleMonitor : IDisposable {
        private readonly IStateMachine _sm;
        private readonly IStateRepo _repo;
        private readonly LifeCycleMonitorOptions _options;
		private readonly Func<AckMonitorNotice, Task> _ackNoticeHandler;
		private readonly Func<AckMonitorNotice, Task>? _failedAckNoticeHandler;
		private readonly Func<InstanceMonitorNotice, Task>? _instanceNoticeHandler;

		private readonly ConcurrentDictionary<string, DateTime> _lastAckNoticeUtc = new();
		private readonly ConcurrentDictionary<string, DateTime> _lastInstanceNoticeUtc = new();

		private static readonly TimeSpan AckPendingFirstThreshold = TimeSpan.FromMinutes(5);
		private static readonly TimeSpan AckDeliveredFirstThreshold = TimeSpan.FromMinutes(30);
		private static readonly TimeSpan AckNoticeRepeat = TimeSpan.FromMinutes(30);

        private Timer? _timer;
        private int _running;

        public DateTime? LastRunUtc { get; private set; }
        public Exception? LastError { get; private set; }
        public bool IsStarted => _timer != null;

		public LifeCycleMonitor(
			IStateMachine sm,
			IStateRepo repo,
			LifeCycleMonitorOptions? options,
			Func<AckMonitorNotice, Task> ackNoticeHandler,
			Func<AckMonitorNotice, Task>? failedAckNoticeHandler = null,
			Func<InstanceMonitorNotice, Task>? instanceNoticeHandler = null) {

            _sm = sm ?? throw new ArgumentNullException(nameof(sm));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
			_options = options ?? new LifeCycleMonitorOptions();
			_ackNoticeHandler = ackNoticeHandler ?? throw new ArgumentNullException(nameof(ackNoticeHandler));
			_failedAckNoticeHandler = failedAckNoticeHandler;
			_instanceNoticeHandler = instanceNoticeHandler;
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
				if (_instanceNoticeHandler != null)
					await ProcessInstanceStuckNotificationsAsync();

				await ProcessAckNotificationsAsync();

				if (_failedAckNoticeHandler != null)
					await ProcessFailedAckNotificationsAsync();

                LastRunUtc = DateTime.UtcNow;
            } catch (Exception ex) {
                LastError = ex;
				Console.WriteLine(ex.StackTrace);
            } finally {
                Interlocked.Exchange(ref _running, 0);
            }
        }

		private async Task ProcessInstanceStuckNotificationsAsync() {
			var fb = await _repo.GetInstancesWithExpiredTimeouts(_options.TimeoutBatchSize);
			if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) return;

			var now = DateTime.UtcNow;
			foreach (var row in fb.Result) {
				try {
					var defVersion = row.GetInt("def_version");
					var externalRef = row.GetString("external_ref") ?? string.Empty;
					var eventCode = row.GetNullableInt("event_code");

					if (defVersion <= 0 || string.IsNullOrWhiteSpace(externalRef)) continue;

					// Re-hydrate instance & state details so we can:
					//  1) skip final states
					//  2) read timeout_minutes + timeout_mode
					var instKey = LifeCycleKeys.Instance(defVersion, externalRef);
					var instFb = await _repo.Get(WorkFlowEntity.Instance, instKey);
					if (instFb == null || !instFb.Status || instFb.Result == null || instFb.Result.Count == 0) continue;
					var instRow = instFb.Result;
					var instanceId = instRow.GetLong("id");
					var currentStateId = instRow.GetInt("current_state");
					var modifiedUtc = instRow.GetDateTime("modified") ?? now;

					if (instanceId <= 0 || currentStateId <= 0) continue;

					var stFb = await _repo.Get(WorkFlowEntity.State, new LifeCycleKey(WorkFlowEntityKeyType.Id, currentStateId));
					if (stFb == null || !stFb.Status || stFb.Result == null || stFb.Result.Count == 0) continue;
					var stRow = stFb.Result;

					var stateFlags = (WorkFlowStateFlag)stRow.GetInt("flags");
					if (stateFlags.HasFlag(WorkFlowStateFlag.IsFinal)) continue; // spec: do nothing for final

					var timeoutMinutes = stRow.GetNullableInt("timeout_minutes");
					if (!timeoutMinutes.HasValue || timeoutMinutes.Value <= 0) continue;
					var timeoutMode = stRow.GetInt("timeout_mode"); // 0=once, 1=repeat
					var stateName = stRow.GetString("display_name") ?? stRow.GetString("name") ?? currentStateId.ToString();

					var stuck = now - modifiedUtc;
					var threshold = TimeSpan.FromMinutes(timeoutMinutes.Value);
					if (stuck < threshold) continue; // safety

					var key = $"inst:{instanceId}:{currentStateId}";
					var repeatEvery = timeoutMode == 1 ? threshold : TimeSpan.MaxValue;
					if (!ShouldNotify(_lastInstanceNoticeUtc, key, repeatEvery, now)) continue;

					await _instanceNoticeHandler!(new InstanceMonitorNotice {
						InstanceId = instanceId,
						DefinitionVersion = defVersion,
						ExternalRef = externalRef,
						CurrentStateId = currentStateId,
						CurrentStateName = stateName,
						TimeoutMinutes = timeoutMinutes,
						TimeoutMode = timeoutMode,
						StateLastChangedUtc = modifiedUtc,
						StuckDuration = stuck,
						TimeoutEventCode = eventCode
					});
				} catch (Exception ex) {
                    Console.WriteLine(ex.StackTrace);
                }
			}
		}

		private async Task ProcessAckNotificationsAsync() {
            var skip = 0;
            var limit = _options.AckBatchSize <= 0 ? 200 : _options.AckBatchSize;

			while (true) {
				// NOTE: We reuse the existing repo query to fetch *not-processed* acks that are old enough.
				// The monitor itself decides what is "overdue" (Pending >= 5m, Delivered >= 30m).
				var fb = await _repo.GetAck(WorkFlowAckFetchMode.DueForRetry, _options.AckMaxRetry, _options.AckRetryAfterMinutes, skip, limit);
                if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) break;

                foreach (var ackRow in fb.Result) {
					await EvaluateAckRowForNoticeAsync(ackRow);
                }

                if (fb.Result.Count < limit) break;
                skip += fb.Result.Count;
            }
        }

		private async Task ProcessFailedAckNotificationsAsync() {
            var skip = 0;
            var limit = _options.AckBatchSize <= 0 ? 200 : _options.AckBatchSize;

			while (true) {
				var fb = await _repo.GetAck(WorkFlowAckFetchMode.Failed, _options.AckMaxRetry, _options.AckRetryAfterMinutes, skip, limit);
                if (fb == null || !fb.Status || fb.Result == null || fb.Result.Count == 0) break;

				foreach (var ackRow in fb.Result) {
					var work = await BuildWorkItemAsync(ackRow);
					if (work == null) continue;

					try {
						var now = DateTime.UtcNow;
						var created = ackRow.GetDateTime("created") ?? now;
						var modified = ackRow.GetDateTime("modified") ?? created;
						var consumer = ackRow.GetInt("consumer");
						var retryCount = ackRow.GetInt("retry_count");
						await _failedAckNoticeHandler!(new AckMonitorNotice {
							Work = work,
							Status = WorkFlowAckStatus.Failed,
							Reason = "ack_failed",
							CreatedUtc = created,
							ModifiedUtc = modified,
							Age = now - created,
							Consumer = consumer,
							RetryCount = retryCount
						});
					} catch (Exception ex) {
                        Console.WriteLine(ex.StackTrace);
                    }
				}

                if (fb.Result.Count < limit) break;
                skip += fb.Result.Count;
            }
        }

		private async Task EvaluateAckRowForNoticeAsync(Dictionary<string, object> ackRow) {
			var ackId = ackRow.GetLong("id");
			var transitionLogId = ackRow.GetLong("transition_log");
			if (ackId <= 0 || transitionLogId <= 0) return;

			var statusInt = ackRow.GetInt("ack_status");
			var status = (WorkFlowAckStatus)statusInt;
			if (status == WorkFlowAckStatus.Processed || status == WorkFlowAckStatus.Failed) return;

			var now = DateTime.UtcNow;
			var created = ackRow.GetDateTime("created") ?? now;
			var modified = ackRow.GetDateTime("modified") ?? created;
			var consumer = ackRow.GetInt("consumer");
			var retryCount = ackRow.GetInt("retry_count");

			TimeSpan age;
			string reason;
			TimeSpan firstThreshold;

			switch (status) {
				case WorkFlowAckStatus.Pending:
					age = now - created;
					reason = "ack_pending_overdue";
					firstThreshold = AckPendingFirstThreshold;
					break;
				case WorkFlowAckStatus.Delivered:
					age = now - modified; // assume Modified is bumped when Delivered is set
					reason = "ack_delivered_not_processed";
					firstThreshold = AckDeliveredFirstThreshold;
					break;
				default:
					return;
			}

			if (age < firstThreshold) return;

			var messageId = ackRow.GetString("message_id") ?? string.Empty;
			var notifyKey = $"ack:{messageId}:{statusInt}";
			if (!ShouldNotify(_lastAckNoticeUtc, notifyKey, AckNoticeRepeat, now)) return;

			var work = await BuildWorkItemAsync(ackRow);
			if (work == null) return;

			try {
				await _ackNoticeHandler(new AckMonitorNotice {
					Work = work,
					Status = status,
					Reason = reason,
					CreatedUtc = created,
					ModifiedUtc = modified,
					Age = age,
					Consumer = consumer,
					RetryCount = retryCount
				});
			} catch (Exception ex) {
				Console.WriteLine(ex.StackTrace);
            }
		}

		private static bool ShouldNotify(ConcurrentDictionary<string, DateTime> cache, string key, TimeSpan repeatEvery, DateTime nowUtc) {
			if (repeatEvery <= TimeSpan.Zero) repeatEvery = TimeSpan.FromMinutes(1);
			if (cache.TryGetValue(key, out var last) && (nowUtc - last) < repeatEvery) return false;
			cache[key] = nowUtc;
			return true;
		}

        private async Task<AckWorkItem?> BuildWorkItemAsync(Dictionary<string, object> ackRow) {
            var ackId = ackRow.GetLong("id");
            var transitionLogId = ackRow.GetLong("transition_log");
            var messageId = ackRow.GetString("message_id") ?? string.Empty;

            var logFb = await _repo.GetTransitionLog(new LifeCycleKey(WorkFlowEntityKeyType.Id, transitionLogId));
            if (logFb == null || !logFb.Status || logFb.Result == null) return null;
            var logRow = logFb.Result;

            var instanceId = logRow.GetLong("instance_id");
            var fromStateId = logRow.GetInt("from_state");
            var toStateId = logRow.GetInt("to_state");
            var eventId = logRow.GetInt("event");
            var actor = logRow.GetString("actor") ?? string.Empty;
            var metadataJson = logRow.GetString("metadata") ?? string.Empty;

            var instFb = await _repo.Get(WorkFlowEntity.Instance, new LifeCycleKey(WorkFlowEntityKeyType.Id, instanceId));
            if (instFb == null || !instFb.Status || instFb.Result == null) return null;
            var instRow = instFb.Result;

            var defVersion = instRow.GetInt("def_version");
            var externalRef = instRow.GetString("external_ref") ?? string.Empty;

            var evFb = await _repo.Get(WorkFlowEntity.Event, new LifeCycleKey(WorkFlowEntityKeyType.Id, eventId));
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
