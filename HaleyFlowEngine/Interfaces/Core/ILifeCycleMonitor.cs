using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface ILifeCycleMonitor : IAsyncDisposable {
        bool IsRunning { get; }
        Task StartAsync(CancellationToken ct = default);
        Task StopAsync(CancellationToken ct = default);
        Task RunOnceAsync(CancellationToken ct = default);
    }
}
