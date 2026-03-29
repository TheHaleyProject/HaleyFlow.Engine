using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System.Text;
using System.Text.Json;
using static Haley.Internal.KeyConstants;

namespace Haley.Internal;

/// <summary>
/// Builds timeline JSON from multiple small focused queries assembled in C#.
/// Replaces the single-CTE approach for better maintainability and detail-level gating.
/// </summary>
internal static class TimelineBuilder {

    public static async Task<string?> BuildAsync(
        IWorkFlowDAL dal, long instanceId, TimelineDetail detail, CancellationToken ct) {

        var load = new DbExecutionLoad(ct);

        // Always fetch instance header and lifecycle transitions.
        var instRow = await dal.LifeCycle.GetInstanceForTimelineAsync(instanceId, load);
        if (instRow == null) return null;

        var lcRows = await dal.LifeCycle.ListLifecyclesForTimelineAsync(instanceId, load);

        // Detailed+ → activities; Admin → hooks in addition.
        DbRows? actRows = null;
        DbRows? hookRows = null;

        if (detail >= TimelineDetail.Detailed)
            actRows = await dal.LifeCycle.ListActivitiesForTimelineAsync(instanceId, load);

        if (detail >= TimelineDetail.Admin)
            hookRows = await dal.LifeCycle.ListHooksForTimelineAsync(instanceId, load);

        // Group by lc_id for fast per-transition lookup.
        var lcIds = new HashSet<long>(lcRows.Count);
        foreach (var r in lcRows) lcIds.Add(r.GetLong(KEY_LIFECYCLE_ID));

        var actByLcId = GroupByLcId(actRows);
        var hookByLcId = GroupByLcId(hookRows);

        // Build JSON.
        using var ms = new MemoryStream(16_000);
        using var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });

        w.WriteStartObject();
        WriteInstance(w, instRow);

        w.WritePropertyName(KEY_TIMELINE);
        w.WriteStartArray();
        for (var i = 0; i < lcRows.Count; i++)
            WriteTransition(w, lcRows[i], i + 1, actByLcId, hookByLcId, detail);
        w.WriteEndArray();

        // Other activities: lc_id = 0 or points to a lifecycle entry that no longer exists.
        w.WritePropertyName(KEY_OTHER_ACTIVITIES);
        w.WriteStartArray();
        if (actByLcId != null) {
            foreach (var kvp in actByLcId) {
                if (lcIds.Contains(kvp.Key)) continue;
                foreach (var act in kvp.Value) WriteActivity(w, act);
            }
        }
        w.WriteEndArray();

        w.WriteEndObject();
        w.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── Writers ──────────────────────────────────────────────────────────────

    private static void WriteInstance(Utf8JsonWriter w, DbRow r) {
        w.WritePropertyName(KEY_INSTANCE);
        w.WriteStartObject();
        w.WriteNumber(KEY_ID, r.GetLong(KEY_ID));
        w.WriteString(KEY_GUID, r.GetString(KEY_GUID));
        w.WriteString(KEY_ENTITY_ID, r.GetString(KEY_ENTITY_ID));
        w.WriteNumber(KEY_DEF_ID, r.GetLong(KEY_DEF_ID));
        w.WriteString(KEY_DEF_NAME, r.GetString(KEY_DEF_NAME));
        w.WriteNumber(KEY_DEF_VERSION_ID, r.GetLong(KEY_DEF_VERSION));   // FK column alias
        w.WriteNumber(KEY_DEF_VERSION, r.GetInt(KEY_DEF_VERSION_NUM)); // version number alias
        w.WriteString(KEY_CURRENT_STATE, r.GetString(KEY_CURRENT_STATE));
        w.WriteString(KEY_LAST_EVENT, r.GetString(KEY_LAST_EVENT));
        WriteDt(w, KEY_CREATED, r[KEY_CREATED]);
        WriteDt(w, KEY_MODIFIED, r[KEY_MODIFIED]);
        w.WriteString(KEY_INSTANCE_STATUS, InstanceStatus((uint)r.GetLong(KEY_FLAGS)));
        var msg = r.GetString(KEY_MESSAGE);
        if (msg != null) w.WriteString(KEY_INSTANCE_MESSAGE, msg);
        else w.WriteNull(KEY_INSTANCE_MESSAGE);
        w.WriteEndObject();
    }

    private static void WriteTransition(
        Utf8JsonWriter w, DbRow lc, int orderNo,
        Dictionary<long, List<DbRow>>? actByLcId,
        Dictionary<long, List<DbRow>>? hookByLcId,
        TimelineDetail detail) {

        var lcId = lc.GetLong(KEY_LIFECYCLE_ID);

        w.WriteStartObject();
        w.WriteNumber(KEY_ORDER_NO, orderNo);
        w.WriteNumber(KEY_LIFECYCLE_ID, lcId);
        WriteDt(w, KEY_CREATED, lc[KEY_CREATED]);
        WriteDt(w, "occurred", lc["occurred"]);
        w.WriteString(KEY_FROM_STATE, lc.GetString(KEY_FROM_STATE));
        w.WriteString(KEY_TO_STATE, lc.GetString(KEY_TO_STATE));
        w.WriteString(KEY_EVENT, lc.GetString(KEY_EVENT));
        w.WriteNumber(KEY_EVENT_CODE, lc.GetInt(KEY_EVENT_CODE));
        w.WriteBoolean(KEY_IS_INITIAL, lc.GetInt(KEY_IS_INITIAL) != 0);
        w.WriteBoolean(KEY_IS_TERMINAL, lc.GetInt(KEY_IS_TERMINAL) != 0);
        w.WriteString(KEY_DISPATCH_MODE, lc.GetString(KEY_DISPATCH_MODE));
        var actor = lc.GetString(KEY_ACTOR);
        if (actor != null) w.WriteString(KEY_ACTOR, actor);
        else w.WriteNull(KEY_ACTOR);

        var hasComplete = lc.GetInt("has_complete") != 0;
        if (hasComplete) {
            w.WritePropertyName(KEY_COMPLETE);
            w.WriteStartObject();
            if (lc["next_event"] is DBNull || lc["next_event"] == null) w.WriteNull(KEY_NEXT_EVENT);
            else w.WriteNumber(KEY_NEXT_EVENT, lc.GetInt(KEY_NEXT_EVENT));

            var ackGuid = lc.GetString(KEY_COMPLETE_ACK_GUID);
            if (!string.IsNullOrWhiteSpace(ackGuid)) w.WriteString(KEY_COMPLETE_ACK_GUID, ackGuid);
            else w.WriteNull(KEY_COMPLETE_ACK_GUID);

            var dispatched = lc.GetInt(KEY_COMPLETE_DISPATCHED) != 0;
            var totalAcks = lc.GetLong(KEY_COMPLETE_TOTAL_ACKS);
            var processedAcks = lc.GetLong(KEY_COMPLETE_PROCESSED_ACKS);
            var failedAcks = lc.GetLong(KEY_COMPLETE_FAILED_ACKS);

            w.WriteBoolean(KEY_COMPLETE_DISPATCHED, dispatched);
            w.WriteNumber(KEY_COMPLETE_TOTAL_ACKS, totalAcks);
            w.WriteNumber(KEY_COMPLETE_PROCESSED_ACKS, processedAcks);
            w.WriteNumber(KEY_COMPLETE_FAILED_ACKS, failedAcks);
            WriteDt(w, KEY_COMPLETE_LAST_TRIGGER, lc[KEY_COMPLETE_LAST_TRIGGER]);
            w.WriteString(KEY_COMPLETE_STATUS, CompleteStatus(dispatched, totalAcks, processedAcks, failedAcks));
            w.WriteEndObject();
        }

        if (detail >= TimelineDetail.Detailed) {
            w.WritePropertyName(KEY_ACTIVITIES);
            w.WriteStartArray();
            if (actByLcId != null && actByLcId.TryGetValue(lcId, out var acts))
                foreach (var a in acts) WriteActivity(w, a);
            w.WriteEndArray();
        }

        if (detail >= TimelineDetail.Admin) {
            w.WritePropertyName(KEY_HOOKS);
            w.WriteStartArray();
            if (hookByLcId != null && hookByLcId.TryGetValue(lcId, out var hooks))
                foreach (var h in hooks) WriteHook(w, h);
            w.WriteEndArray();
        }

        w.WriteEndObject();
    }

    private static void WriteActivity(Utf8JsonWriter w, DbRow r) {
        w.WriteStartObject();
        w.WriteNumber(KEY_RUNTIME_ID, r.GetLong(KEY_RUNTIME_ID));
        w.WriteNumber(KEY_LC_ID, r.GetLong(KEY_LC_ID));
        w.WriteString(KEY_ACTIVITY, r.GetString(KEY_ACTIVITY));
        w.WriteString(KEY_LABEL, r.GetString(KEY_LABEL));
        w.WriteString(KEY_ACTOR_ID, r.GetString(KEY_ACTOR_ID));
        w.WriteString(KEY_STATUS, r.GetString(KEY_STATUS));
        WriteDt(w, KEY_CREATED, r[KEY_CREATED]);
        WriteDt(w, KEY_MODIFIED, r[KEY_MODIFIED]);
        w.WriteBoolean(KEY_FROZEN, r.GetInt(KEY_FROZEN) != 0);
        w.WriteEndObject();
    }

    private static void WriteHook(Utf8JsonWriter w, DbRow r) {
        w.WriteStartObject();
        w.WriteNumber(KEY_HOOK_LC_ID, r.GetLong(KEY_HOOK_LC_ID));
        w.WriteNumber(KEY_LC_ID, r.GetLong(KEY_LC_ID));
        w.WriteString(KEY_ROUTE, r.GetString(KEY_ROUTE));
        w.WriteString(KEY_LABEL, r.GetString(KEY_LABEL));
        w.WriteNumber("hook_type", r.GetInt("hook_type"));
        w.WriteBoolean(KEY_ON_ENTRY, r.GetInt(KEY_ON_ENTRY) != 0);
        w.WriteNumber(KEY_ORDER_SEQ, r.GetInt(KEY_ORDER_SEQ));
        w.WriteBoolean(KEY_DISPATCHED, r.GetInt(KEY_DISPATCHED) != 0);
        w.WriteNumber(KEY_TOTAL_ACKS, r.GetLong(KEY_TOTAL_ACKS));
        w.WriteNumber(KEY_PROCESSED_ACKS, r.GetLong(KEY_PROCESSED_ACKS));
        w.WriteNumber(KEY_FAILED_ACKS, r.GetLong(KEY_FAILED_ACKS));
        w.WriteNumber(KEY_MAX_RETRIES, r.GetLong(KEY_MAX_RETRIES));
        w.WriteNumber(KEY_TOTAL_TRIGGERS, r.GetLong(KEY_TOTAL_TRIGGERS));
        WriteDt(w, KEY_LAST_TRIGGER, r[KEY_LAST_TRIGGER]);
        w.WriteEndObject();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void WriteDt(Utf8JsonWriter w, string name, object? val) {
        if (val == null || val == DBNull.Value) { w.WriteNull(name); return; }
        if (val is DateTime dt) { w.WriteString(name, dt); return; }
        if (DateTime.TryParse(Convert.ToString(val), out var parsed)) { w.WriteString(name, parsed); return; }
        w.WriteNull(name);
    }

    private static string InstanceStatus(uint f) {
        if ((f & 16) != 0) return nameof(LifeCycleInstanceFlag.Archived);
        if ((f & 8) != 0) return nameof(LifeCycleInstanceFlag.Failed);
        if ((f & 4) != 0) return nameof(LifeCycleInstanceFlag.Completed);
        if ((f & 2) != 0) return nameof(LifeCycleInstanceFlag.Suspended);
        if ((f & 1) != 0) return nameof(LifeCycleInstanceFlag.Active);
        return nameof(LifeCycleInstanceFlag.None);
    }

    private static string CompleteStatus(bool dispatched, long totalAcks, long processedAcks, long failedAcks) {
        if (!dispatched) return "Ready";
        if (failedAcks > 0) return "Failed";
        if (totalAcks > 0 && processedAcks >= totalAcks) return "Processed";
        if (processedAcks > 0) return "Partial";
        if (totalAcks > 0) return "Pending";
        return "Dispatched";
    }

    private static Dictionary<long, List<DbRow>>? GroupByLcId(DbRows? rows) {
        if (rows == null || rows.Count == 0) return null;
        var d = new Dictionary<long, List<DbRow>>(rows.Count);
        foreach (var r in rows) {
            var id = r.GetLong(KEY_LC_ID);
            if (!d.TryGetValue(id, out var list)) { list = new List<DbRow>(); d[id] = list; }
            list.Add(r);
        }
        return d;
    }
}
