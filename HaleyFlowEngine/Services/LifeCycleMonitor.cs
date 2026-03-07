using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    // LifeCycleMonitor is a thin periodic-timer wrapper. Its only job is to call a delegate
    // (_runOnce) on a fixed interval. All actual workflow logic lives in WorkFlowEngine.RunMonitorAsync.
    // The monitor does not know about ACKs, consumers, or blueprints — it just fires the tick.
    // This separation makes it easy to test the monitor independently and swap the scheduler later.
    internal sealed class LifeCycleMonitor : ILifeCycleMonitor {
        private readonly TimeSpan _interval;
        private readonly Func<CancellationToken, Task> _runOnce;
        private readonly Action<Exception>? _onError;

        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;
        private Task? _loop;
        // _running: 0 = stopped, 1 = started. Compared-and-swapped atomically to prevent double StartAsync.
        private int _running;
        // _runGate: 0 = idle, 1 = busy. Guards RunOnceAsync from concurrent overlapping executions.
        // PeriodicTimer fires its next tick as soon as the interval elapses — it doesn't wait for the
        // previous tick's work to finish. Without this gate, a slow RunOnce could stack up concurrently.
        private int _runGate;

        public bool IsRunning => Volatile.Read(ref _running) == 1;

        public LifeCycleMonitor(TimeSpan interval, Func<CancellationToken, Task> runOnce, Action<Exception>? onError = null) {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            _interval = interval;
            _runOnce = runOnce ?? throw new ArgumentNullException(nameof(runOnce));
            _onError = onError;
        }

        // Starts the monitor loop. CompareExchange ensures only the first caller wins —
        // subsequent calls return immediately without creating a second timer or loop task.
        // The loop task is fire-and-forget (stored in _loop for StopAsync to await on shutdown).
        public Task StartAsync(CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return Task.CompletedTask;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _timer = new PeriodicTimer(_interval);
            _loop = LoopAsync(_cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _running, 0, 1) != 1) return;

            try { _cts?.Cancel(); } catch { }
            var loop = _loop;
            if (loop != null) await loop;

            _timer?.Dispose();
            _timer = null;
            _cts?.Dispose();
            _cts = null;
            _loop = null;
            Interlocked.Exchange(ref _runGate, 0);
        }

        // Executes one monitor cycle. The _runGate ensures only one cycle runs at a time —
        // if a prior cycle is still running when the next tick fires, the new tick is skipped silently.
        // Errors are swallowed (passed to _onError) so the loop continues on the next tick.
        // Cancellation propagates out because it means the monitor is shutting down.
        public async Task RunOnceAsync(CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _runGate, 1, 0) != 0) return;

            try {
                await _runOnce(ct);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                if (_onError != null) _onError.Invoke(ex);
            } finally {
                Interlocked.Exchange(ref _runGate, 0);
            }
        }

        // Simple PeriodicTimer loop: wait for tick → run one cycle → repeat.
        // PeriodicTimer.WaitForNextTickAsync returns false when the timer is disposed (on StopAsync),
        // so the loop exits cleanly. OperationCanceledException on shutdown is expected and suppressed.
        private async Task LoopAsync(CancellationToken ct) {
            try {
                while (!ct.IsCancellationRequested && _timer != null) {
                    if (!await _timer.WaitForNextTickAsync(ct)) break;
                    await RunOnceAsync(ct);
                }
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                // expected on stop
            } catch (Exception ex) {
                _onError?.Invoke(ex);
            }
        }

        public async ValueTask DisposeAsync() { await StopAsync(CancellationToken.None); }
    }
}