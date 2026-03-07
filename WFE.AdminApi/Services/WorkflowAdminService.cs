using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using Microsoft.Extensions.Options;
using WFE.AdminApi.Configuration;

namespace WFE.AdminApi.Services;

internal sealed class WorkflowAdminService : IWorkflowAdminService, IAsyncDisposable {
    private const string EngineAdapterKey = "lce_test";
    private const string ConsumerAdapterKey = "lcc_test";

    private readonly WorkflowAdminOptions _options;
    private readonly AdapterGateway _agw;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IWorkFlowEngine? _engine;

    public WorkflowAdminService(IOptions<WorkflowAdminOptions> options, IAdapterGateway agw) {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _agw = agw as AdapterGateway ?? throw new ArgumentException("AdapterGateway implementation is required.", nameof(agw));
    }

    public async Task<LifeCycleInstanceData?> GetInstanceAsync(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var key = BuildInstanceKey(envCode, defName, entityId, instanceGuid);
        return await _engine!.GetInstanceDataAsync(key, ct);
    }

    public async Task<string?> GetTimelineJsonAsync(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var key = BuildInstanceKey(envCode, defName, entityId, instanceGuid);
        return await _engine!.GetTimelineJsonAsync(key, ct);
    }

    public async Task<IReadOnlyList<InstanceRefItem>> GetInstanceRefsAsync(
        int? envCode,
        string defName,
        LifeCycleInstanceFlag flags,
        int skip,
        int take,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(defName)) throw new ArgumentException("Definition name is required.", nameof(defName));
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);
        return await _engine!.GetInstanceRefsAsync(ResolveEnvCode(envCode), defName.Trim(), flags, normalizedSkip, normalizedTake, ct);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetEngineEntitiesAsync(
        string? defName,
        bool runningOnly,
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);

        const string sql = @"
SELECT
    i.id,
    i.guid,
    i.entity_id,
    d.name AS def_name,
    i.def_version,
    i.current_state,
    s.name AS current_state_name,
    s.flags AS state_flags,
    i.flags AS instance_flags,
    i.created,
    i.modified
FROM instance i
JOIN definition d ON d.id = i.def_id
JOIN environment e ON e.id = d.env
JOIN state s ON s.id = i.current_state
WHERE e.code = @envCode
  AND (@defName = '' OR d.name = @defName)
  AND (@runningOnly = 0 OR (s.flags & 2) = 0)
ORDER BY i.id DESC
LIMIT @take OFFSET @skip;";

        var rows = await _agw.RowsAsync(
            EngineAdapterKey,
            sql,
            new DbExecutionLoad(ct),
            ("envCode", _options.EnvCode),
            ("defName", defName?.Trim() ?? string.Empty),
            ("runningOnly", runningOnly ? 1 : 0),
            ("take", normalizedTake),
            ("skip", normalizedSkip));

        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetPendingAcksAsync(
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);

        const string sql = @"
SELECT
    ac.ack_id,
    a.guid AS ack_guid,
    ac.consumer,
    ac.status,
    ac.next_due,
    ac.trigger_count,
    ac.last_trigger,
    ac.created,
    ac.modified,
    a.created AS ack_created,
    i.guid AS instance_guid,
    i.entity_id,
    d.name AS def_name,
    hr.name AS hook_route
FROM ack_consumer ac
JOIN ack a ON a.id = ac.ack_id
LEFT JOIN lc_ack la ON la.ack_id = ac.ack_id
LEFT JOIN lifecycle lc ON lc.id = la.lc_id
LEFT JOIN instance li ON li.id = lc.instance_id
LEFT JOIN hook_ack ha ON ha.ack_id = ac.ack_id
LEFT JOIN hook hk ON hk.id = ha.hook_id
LEFT JOIN hook_route hr ON hr.id = hk.route_id
LEFT JOIN instance hi ON hi.id = hk.instance_id
LEFT JOIN instance i ON i.id = COALESCE(li.id, hi.id)
LEFT JOIN definition d ON d.id = i.def_id
LEFT JOIN environment e ON e.id = d.env
WHERE ac.status IN (1, 2)
  AND (e.code IS NULL OR e.code = @envCode)
ORDER BY ac.next_due ASC, ac.ack_id DESC
LIMIT @take OFFSET @skip;";

        var rows = await _agw.RowsAsync(
            EngineAdapterKey,
            sql,
            new DbExecutionLoad(ct),
            ("envCode", _options.EnvCode),
            ("take", normalizedTake),
            ("skip", normalizedSkip));

        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerWorkflowsAsync(
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);

        const string sql = @"
SELECT
    w.id,
    w.ack_guid,
    w.entity_id,
    w.kind,
    w.consumer_id,
    w.def_id,
    w.def_version_id,
    w.instance_guid,
    w.event_code,
    w.route,
    w.occurred,
    w.created,
    i.status AS inbox_status,
    i.attempt_count,
    o.status AS outbox_status,
    o.current_outcome,
    o.next_retry_at
FROM workflow w
LEFT JOIN inbox i ON i.wf_id = w.id
LEFT JOIN outbox o ON o.wf_id = w.id
ORDER BY w.id DESC
LIMIT @take OFFSET @skip;";

        var rows = await _agw.RowsAsync(
            ConsumerAdapterKey,
            sql,
            new DbExecutionLoad(ct),
            ("take", normalizedTake),
            ("skip", normalizedSkip));

        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerInboxAsync(
        int? status,
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);

        const string sql = @"
SELECT
    i.wf_id,
    i.status,
    i.attempt_count,
    i.last_error,
    i.received_at,
    i.modified,
    w.entity_id,
    w.instance_guid,
    w.kind,
    w.route,
    w.event_code
FROM inbox i
JOIN workflow w ON w.id = i.wf_id
WHERE (@status < 0 OR i.status = @status)
ORDER BY i.modified DESC
LIMIT @take OFFSET @skip;";

        var rows = await _agw.RowsAsync(
            ConsumerAdapterKey,
            sql,
            new DbExecutionLoad(ct),
            ("status", status ?? -1),
            ("take", normalizedTake),
            ("skip", normalizedSkip));

        return ToDictionaries(rows);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetConsumerOutboxAsync(
        int? status,
        int skip,
        int take,
        CancellationToken ct) {
        await EnsureInitializedAsync(ct);
        var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take);

        const string sql = @"
SELECT
    o.wf_id,
    o.current_outcome,
    o.status,
    o.next_retry_at,
    o.last_error,
    o.modified,
    w.entity_id,
    w.instance_guid,
    w.kind,
    w.route,
    w.event_code
FROM outbox o
JOIN workflow w ON w.id = o.wf_id
WHERE (@status < 0 OR o.status = @status)
ORDER BY o.modified DESC
LIMIT @take OFFSET @skip;";

        var rows = await _agw.RowsAsync(
            ConsumerAdapterKey,
            sql,
            new DbExecutionLoad(ct),
            ("status", status ?? -1),
            ("take", normalizedTake),
            ("skip", normalizedSkip));

        return ToDictionaries(rows);
    }

    public async Task<Dictionary<string, object?>> GetSummaryAsync(CancellationToken ct) {
        await EnsureInitializedAsync(ct);

        const string engineTotalSql = @"
SELECT COUNT(1)
FROM instance i
JOIN definition d ON d.id = i.def_id
JOIN environment e ON e.id = d.env
WHERE e.code = @envCode;";

        const string engineRunningSql = @"
SELECT COUNT(1)
FROM instance i
JOIN definition d ON d.id = i.def_id
JOIN environment e ON e.id = d.env
JOIN state s ON s.id = i.current_state
WHERE e.code = @envCode
  AND (s.flags & 2) = 0;";

        const string pendingAckSql = @"
SELECT COUNT(1)
FROM ack_consumer
WHERE status IN (1, 2);";

        const string inboxPendingSql = @"SELECT COUNT(1) FROM inbox WHERE status IN (1, 2);";
        const string outboxPendingSql = @"SELECT COUNT(1) FROM outbox WHERE status = 1;";

        var engineTotal = await _agw.ScalarAsync<long>(EngineAdapterKey, engineTotalSql, new DbExecutionLoad(ct), ("envCode", _options.EnvCode));
        var engineRunning = await _agw.ScalarAsync<long>(EngineAdapterKey, engineRunningSql, new DbExecutionLoad(ct), ("envCode", _options.EnvCode));
        var enginePendingAcks = await _agw.ScalarAsync<long>(EngineAdapterKey, pendingAckSql, new DbExecutionLoad(ct));
        var consumerInboxPending = await _agw.ScalarAsync<long>(ConsumerAdapterKey, inboxPendingSql, new DbExecutionLoad(ct));
        var consumerOutboxPending = await _agw.ScalarAsync<long>(ConsumerAdapterKey, outboxPendingSql, new DbExecutionLoad(ct));

        return new Dictionary<string, object?> {
            ["envCode"] = _options.EnvCode,
            ["engineTotalInstances"] = engineTotal ,
            ["engineRunningInstances"] = engineRunning ,
            ["enginePendingAcks"] = enginePendingAcks ,
            ["consumerPendingInbox"] = consumerInboxPending ,
            ["consumerPendingOutbox"] = consumerOutboxPending 
        };
    }

    public async ValueTask DisposeAsync() {
        if (_engine is IAsyncDisposable disposableEngine) {
            try { await disposableEngine.DisposeAsync(); } catch { }
        }
        _initLock.Dispose();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) {
        if (_engine != null) return;

        await _initLock.WaitAsync(ct);
        try {
            if (_engine != null) return;
            var engineMaker = new WorkFlowEngineMaker().WithAdapterKey(EngineAdapterKey);
            _engine = await engineMaker.Build(_agw);
        } finally {
            _initLock.Release();
        }
    }

    private LifeCycleInstanceKey BuildInstanceKey(
        int? envCode,
        string? defName,
        string? entityId,
        string? instanceGuid) {
        if (!string.IsNullOrWhiteSpace(instanceGuid)) {
            return new LifeCycleInstanceKey {
                InstanceGuid = instanceGuid.Trim()
            };
        }

        if (string.IsNullOrWhiteSpace(defName)) throw new ArgumentException("Definition name is required when instanceGuid is not supplied.", nameof(defName));
        if (string.IsNullOrWhiteSpace(entityId)) throw new ArgumentException("Entity id is required when instanceGuid is not supplied.", nameof(entityId));

        return new LifeCycleInstanceKey {
            EnvCode = ResolveEnvCode(envCode),
            DefName = defName.Trim(),
            EntityId = entityId.Trim()
        };
    }

    private int ResolveEnvCode(int? envCode)
        => envCode.GetValueOrDefault(_options.EnvCode);

    private (int skip, int take) NormalizePaging(int skip, int take) {
        var normalizedSkip = skip < 0 ? 0 : skip;
        var fallbackTake = _options.DefaultTake > 0 ? _options.DefaultTake : 50;
        var normalizedTake = take <= 0 ? fallbackTake : take;
        var maxTake = _options.MaxTake > 0 ? _options.MaxTake : 500;
        if (normalizedTake > maxTake) normalizedTake = maxTake;
        return (normalizedSkip, normalizedTake);
    }

    private static IReadOnlyList<Dictionary<string, object?>> ToDictionaries(DbRows rows) {
        var result = new List<Dictionary<string, object?>>(rows.Count);
        for (var i = 0; i < rows.Count; i++) {
            var row = rows[i];
            var item = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in row) {
                item[entry.Key] = entry.Value == DBNull.Value ? null : entry.Value;
            }
            result.Add(item);
        }
        return result;
    }
}
