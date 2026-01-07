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
    internal sealed class LifeCycleMonitor : ILifeCycleMonitor {
        private readonly TimeSpan _interval;
        private readonly Func<CancellationToken, Task> _runOnce;
        private readonly Action<Exception>? _onError;

        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private int _running;
        private int _runGate;

        public bool IsRunning => Volatile.Read(ref _running) == 1;

        public LifeCycleMonitor(TimeSpan interval, Func<CancellationToken, Task> runOnce, Action<Exception>? onError = null) {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            _interval = interval;
            _runOnce = runOnce ?? throw new ArgumentNullException(nameof(runOnce));
            _onError = onError;
        }

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

        private async Task LoopAsync(CancellationToken ct) {
            try {
                while (!ct.IsCancellationRequested && _timer != null) {
                    if (!await _timer.WaitForNextTickAsync(ct)) break;
                    await RunOnceAsync(ct);
                }
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                // expected on stop
            } catch (Exception ex) {
                if (_onError != null) _onError.Invoke(ex);
            }
        }

        public async ValueTask DisposeAsync() { await StopAsync(CancellationToken.None); }
    }
}