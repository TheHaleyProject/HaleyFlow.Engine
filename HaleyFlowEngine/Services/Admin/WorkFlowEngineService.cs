using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System.Diagnostics;

namespace Haley.Services;

public class WorkFlowEngineService : IWorkFlowEngineService, IAsyncDisposable {

    private readonly EngineServiceOptions _options;
    private readonly AdapterGateway _agw;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _runtimeInitLock = new(1, 1);

    private IWorkFlowEngine? _engine;
    private bool _runtimeStarted;

    public WorkFlowEngineService(EngineServiceOptions options, IAdapterGateway agw) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _agw = agw as AdapterGateway ?? throw new ArgumentException("AdapterGateway implementation is required.", nameof(agw));
    }

    public async Task<LifeCycleInstanceData?> GetInstanceAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var key = BuildInstanceKey(envCode, defName, entityId, instanceGuid);
        return await _engine!.GetInstanceDataAsync(key, ct);
    }

    public async Task<string?> GetTimelineJsonAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, TimelineDetail detail, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var key = BuildInstanceKey(envCode, defName, entityId, instanceGuid);
        return await _engine!.GetTimelineJsonAsync(key, detail, ct);
    }

    public async Task<string?> GetTimelineHtmlAsync(int? envCode, string? defName, string? entityId, string? instanceGuid, string? displayName, TimelineDetail detail, CancellationToken ct) {
        var json = await GetTimelineJsonAsync(envCode, defName, entityId, instanceGuid, detail, ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        return TimelineHtmlRenderer.Render(json, displayName?.Trim(), detail);
    }

    public async Task<IReadOnlyList<InstanceRefItem>> GetInstanceRefsAsync(int? envCode, string defName, LifeCycleInstanceFlag flags, int skip, int take, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(defName)) throw new ArgumentException("Definition name is required.", nameof(defName));
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        return await _engine!.GetInstanceRefsAsync(RequireEnvCode(envCode), defName.Trim(), flags, normalizedSkip, normalizedTake, ct);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetEngineEntitiesAsync(int envCode, string? defName, bool runningOnly, int skip, int take, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await _engine!.ListInstancesAsync(envCode, defName?.Trim(), runningOnly, normalizedSkip, normalizedTake, ct);
        return ToEngineInstanceDictionaries(rows);
    }
    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetEngineInstancesByStatusAsync(int envCode, string? defName, LifeCycleInstanceFlag statusFlags, int skip, int take, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await _engine!.ListInstancesByStatusAsync(envCode, defName?.Trim(), statusFlags, normalizedSkip, normalizedTake, ct);
        return ToEngineInstanceDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetPendingAcksAsync(int envCode, int skip, int take, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        var rows = await _engine!.ListPendingAcksAsync(envCode, normalizedSkip, normalizedTake, ct);
        return rows.ToDictionaries();
    }

    public async Task<Dictionary<string, object?>> GetSummaryAsync(int envCode, CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var s = await _engine!.GetSummaryAsync(envCode, ct);
        return new Dictionary<string, object?> {
            ["envCode"] = envCode,
            ["engineTotalInstances"] = s.TotalInstances,
            ["engineRunningInstances"] = s.RunningInstances,
            ["enginePendingAcks"] = s.PendingAcks,
        };
    }

    public async Task<Dictionary<string, object?>> GetHealthAsync(CancellationToken ct) {
        await EnsureInitializedAsync(ct);

        Dictionary<string, object?> engineCheck;
        var sw = Stopwatch.StartNew();
        try {
            var h = await _engine!.GetHealthAsync(ct);
            sw.Stop();
            engineCheck = new Dictionary<string, object?> {
                ["name"] = "engine",
                ["status"] = "healthy",
                ["responseTimeMs"] = sw.ElapsedMilliseconds,
                ["isMonitorRunning"] = h.IsMonitorRunning,
                ["monitorIntervalSeconds"] = h.MonitorInterval.TotalSeconds,
                ["consumerTtlSeconds"] = h.ConsumerTtlSeconds,
                ["totalConsumers"] = h.TotalConsumers,
                ["aliveConsumers"] = h.AliveConsumers,
                ["downConsumers"] = h.DownConsumers,
                ["dueLifecyclePending"] = h.DueLifecyclePendingCount,
                ["dueLifecycleDelivered"] = h.DueLifecycleDeliveredCount,
                ["dueHookPending"] = h.DueHookPendingCount,
                ["dueHookDelivered"] = h.DueHookDeliveredCount,
                ["staleInstances"] = h.DefaultStateStaleCount,
                ["runtimeStarted"] = _runtimeStarted
            };
        } catch (Exception ex) {
            sw.Stop();
            engineCheck = new Dictionary<string, object?> {
                ["name"] = "engine",
                ["status"] = "unhealthy",
                ["responseTimeMs"] = sw.ElapsedMilliseconds,
                ["error"] = ex.Message
            };
        }

        var allHealthy = (string?)engineCheck["status"] == "healthy";

        return new Dictionary<string, object?> {
            ["status"] = allHealthy ? "healthy" : "unhealthy",
            ["checkedAt"] = DateTimeOffset.UtcNow,
            ["checks"] = new[] { engineCheck }
        };
    }

    public async Task<Dictionary<string, object?>> EnsureHostInitializedAsync(CancellationToken ct) {
        var wasRuntimeStarted = _runtimeStarted;
        await EnsureInitializedAsync(ct);

        return new Dictionary<string, object?> {
            ["status"] = "ok",
            ["checkedAt"] = DateTimeOffset.UtcNow,
            ["engineInitialized"] = _engine != null,
            ["runtimeStarted"] = _runtimeStarted,
            ["alreadyRunning"] = wasRuntimeStarted
        };
    }

    public async Task<bool> SuspendInstanceAsync(string instanceGuid, string? message, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentException("instanceGuid is required.", nameof(instanceGuid));

        await EnsureInitializedAsync(ct);
        return await _engine!.SuspendInstanceAsync(instanceGuid.Trim(), message, ct);
    }

    public async Task<bool> ResumeInstanceAsync(string instanceGuid, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentException("instanceGuid is required.", nameof(instanceGuid));

        await EnsureInitializedAsync(ct);
        return await _engine!.ResumeInstanceAsync(instanceGuid.Trim(), ct);
    }

    public async Task<bool> FailInstanceAsync(string instanceGuid, string? message, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentException("instanceGuid is required.", nameof(instanceGuid));

        await EnsureInitializedAsync(ct);
        return await _engine!.FailInstanceAsync(instanceGuid.Trim(), message, ct);
    }
    public async Task<LifeCycleTriggerResult> ReopenInstanceAsync(string instanceGuid, string actor, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(instanceGuid)) throw new ArgumentException("instanceGuid is required.", nameof(instanceGuid));

        await EnsureInitializedAsync(ct);
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "wfe.service" : actor.Trim();
        return await _engine!.ReopenAsync(instanceGuid.Trim(), normalizedActor, ct);
    }

    public async Task<IWorkFlowEngine> GetEngineAsync(CancellationToken ct = default) {
        await EnsureInitializedAsync(ct);
        return _engine!;
    }

    public async ValueTask DisposeAsync() {
        if (_engine != null) {
            try { await _engine.StopMonitorAsync(CancellationToken.None); } catch { }
        }

        if (_engine is IAsyncDisposable disposableEngine) {
            try { await disposableEngine.DisposeAsync(); } catch { }
        }

        _initLock.Dispose();
        _runtimeInitLock.Dispose();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) {
        if (_engine == null) {
            await _initLock.WaitAsync(ct);
            try {
                if (_engine == null) {
                    var engineMaker = new WorkFlowEngineMaker().WithAdapterKey(_options.EngineAdapterKey);
                    engineMaker.Options = _options;
                    _engine = await engineMaker.Build(_agw);
                }
            } catch (Exception) {
                throw; //Very important.. because without this, the engine might silently be failing and we will never know the issue..
            } finally {
                _initLock.Release();
            }
        }

        await EnsureRuntimeStartedAsync(ct);
    }

    private async Task EnsureRuntimeStartedAsync(CancellationToken ct) {
        if (_runtimeStarted) return;

        await _runtimeInitLock.WaitAsync(ct);
        try {
            if (_runtimeStarted) return;
            if (_engine == null) throw new InvalidOperationException("Engine is not initialized.");

            // Engine runtime start only.
            // Consumer/environment registration belongs to consumer-side startup through ILifeCycleEngineProxy
            // (in-process or remote), not engine host startup.
            await _engine.StartMonitorAsync(ct); 
            _runtimeStarted = true;
        } catch (Exception) {
            throw; //Very important.. because without this, the engine might silently be failing and we will never know the issue..
        } finally {
            _runtimeInitLock.Release();
        }
    }

    private static IReadOnlyList<Dictionary<string, object?>> ToEngineInstanceDictionaries(DbRows rows) {
        var items = rows.ToDictionaries();
        for (var i = 0; i < items.Count; i++) {
            var item = items[i];
            var flags = ToUInt(item, "instance_flags");
            var statuses = GetInstanceStatuses(flags);
            item["instance_status"] = statuses[0];
            item["instance_statuses"] = statuses;
        }

        return items;
    }

    private static uint ToUInt(Dictionary<string, object?> item, string key) {
        if (!item.TryGetValue(key, out var value) || value == null) return 0;

        try {
            return Convert.ToUInt32(value);
        } catch {
            return 0;
        }
    }

    private static List<string> GetInstanceStatuses(uint flags) {
        // Fail-safe projection: terminal flags dominate, so API never reports Active with terminal states.
        if ((flags & (uint)LifeCycleInstanceFlag.Archived) != 0)
            return new List<string>(1) { nameof(LifeCycleInstanceFlag.Archived) };
        if ((flags & (uint)LifeCycleInstanceFlag.Failed) != 0)
            return new List<string>(1) { nameof(LifeCycleInstanceFlag.Failed) };
        if ((flags & (uint)LifeCycleInstanceFlag.Completed) != 0)
            return new List<string>(1) { nameof(LifeCycleInstanceFlag.Completed) };

        var statuses = new List<string>(2);
        if ((flags & (uint)LifeCycleInstanceFlag.Suspended) != 0) statuses.Add(nameof(LifeCycleInstanceFlag.Suspended));
        if ((flags & (uint)LifeCycleInstanceFlag.Active) != 0) statuses.Add(nameof(LifeCycleInstanceFlag.Active));

        if (statuses.Count == 0) statuses.Add(nameof(LifeCycleInstanceFlag.None));
        return statuses;
    }
    private LifeCycleInstanceKey BuildInstanceKey(int? envCode, string? defName, string? entityId, string? instanceGuid) {
        if (!string.IsNullOrWhiteSpace(instanceGuid)) {
            return new LifeCycleInstanceKey {
                InstanceGuid = instanceGuid.Trim()
            };
        }

        if (string.IsNullOrWhiteSpace(defName)) throw new ArgumentException("Definition name is required when instanceGuid is not supplied.", nameof(defName));
        if (string.IsNullOrWhiteSpace(entityId)) throw new ArgumentException("Entity id is required when instanceGuid is not supplied.", nameof(entityId));

        return new LifeCycleInstanceKey {
            EnvCode = RequireEnvCode(envCode),
            DefName = defName.Trim(),
            EntityId = entityId.Trim()
        };
    }

    private static int RequireEnvCode(int? envCode) {
        if (!envCode.HasValue) throw new ArgumentException("envCode is required when instanceGuid is not supplied.", nameof(envCode));
        return envCode.Value;
    }

    private static (int skip, int take) NormalizePaging(int skip, int take) {
        var normalizedSkip = skip < 0 ? 0 : skip;
        var normalizedTake = take <= 0 ? 50 : take;
        const int maxTake = 500;
        if (normalizedTake > maxTake) normalizedTake = maxTake;
        return (normalizedSkip, normalizedTake);
    }
}


