using Haley.Models;
using Haley.Utils;
using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    /// <summary>
    /// Minimal monitor: asks AckManager for pending events and re-raises them.
    /// (AckManager is responsible for producing the correct event types: transition/hook)
    /// </summary>
    public sealed class InstanceMonitor : IInstanceMonitor {
        private readonly IAckManager _acks;
        private readonly Func<ILifeCycleEvent, Task> _publish;
        private readonly int _take;

        public InstanceMonitor(IAckManager acks, Func<ILifeCycleEvent, Task> publish, int take = 100) {
            _acks = acks ?? throw new ArgumentNullException(nameof(acks));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _take = take;
        }

        public async Task RunOnceAsync(CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            var load = new DbExecutionLoad(ct);
            var pending = await _acks.GetPendingDispatchAsync(DateTime.UtcNow, _take, load).ConfigureAwait(false);

            for (int i = 0; i < pending.Count; i++) {
                ct.ThrowIfCancellationRequested();
                await _publish(pending[i]).ConfigureAwait(false);
            }
        }
    }
}