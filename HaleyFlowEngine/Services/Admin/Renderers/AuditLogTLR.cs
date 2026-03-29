using Haley.Enums;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Haley.Services;

/// <summary>
/// Converts a timeline JSON string into a self-contained HTML page.
/// Design: Audit Log (C) — dense table with per-row expand/collapse detail panel and compact column toggle.
/// </summary>
internal static class AuditLogTLR {

    public static string Render(string timelineJson, string? displayName = null, TimelineDetail detail = TimelineDetail.Detailed, string? color = null) {
        using var doc = JsonDocument.Parse(timelineJson);
        var root = doc.RootElement;
        var pageTitle = BuildPageTitle(root);

        var sb = new StringBuilder(20_000);
        WriteHead(sb, pageTitle, color);

        if (root.TryGetProperty("instance", out var inst) &&
            root.TryGetProperty("timeline", out var tl) &&
            tl.ValueKind == JsonValueKind.Array) {

            var items = tl.EnumerateArray().ToList();
            WriteHeader(sb, inst, displayName);
            WriteMetaBar(sb, inst, items.Count);
            WriteTable(sb, items, detail);
        }

        sb.Append("""
</div>
</body>
</html>
""");
        return sb.ToString();
    }

    // ── Head ─────────────────────────────────────────────────────────────────

    private static void WriteHead(StringBuilder sb, string pageTitle, string? color) {
        sb.Append("""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
""");
        sb.Append($"  <title>{E(pageTitle)}</title>\n");
        sb.Append("""
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    :root {
      --bg:       #f9fafb;
      --white:    #ffffff;
      --border:   #e5e7eb;
      --text:     #111827;
      --muted:    #6b7280;
      --light:    #9ca3af;
      --acc:      #2563eb;
      --acc-lt:   #eff6ff;
      --acc-bd:   #bfdbfe;
      --green:    #059669;
      --green-lt: #ecfdf5;
      --green-bd: #a7f3d0;
      --amber:    #b45309;
      --amber-lt: #fffbeb;
      --amber-bd: #fde68a;
      --red:      #dc2626;
      --red-lt:   #fef2f2;
      --red-bd:   #fecaca;
      --teal:     #0f766e;
      --teal-lt:  #f0fdfa;
      --teal-bd:  #99f6e4;
      --mono:     'Cascadia Code', 'Fira Mono', 'Consolas', monospace;
    }
    html, body { height: 100%; background: var(--bg); color: var(--text); font-family: -apple-system, 'Segoe UI', sans-serif; font-size: 13px; }
    body { display: flex; flex-direction: column; overflow: hidden; }
    ::-webkit-scrollbar { width: 5px; height: 5px; }
    ::-webkit-scrollbar-thumb { background: #d1d5db; border-radius: 3px; }

    .shell  { max-width: 1000px; width: 100%; margin: 0 auto; padding: 20px 16px 0; height: 100vh; display: flex; flex-direction: column; }
    .scroll { flex: 1; overflow-y: auto; padding-bottom: 32px; }

    /* ── Header ── */
    .hdr        { background: var(--white); border: 1px solid var(--border); border-radius: 10px; padding: 16px 20px; margin-bottom: 12px; display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
    .hdr-left   { min-width: 0; flex: 1; }
    .hdr-tag    { font-size: 10px; text-transform: uppercase; letter-spacing: .1em; font-weight: 700; color: var(--acc); margin-bottom: 4px; }
    .hdr-entity { font-size: 18px; font-weight: 800; color: var(--text); word-break: break-all; }
    .hdr-guid   { font-size: 11px; font-family: var(--mono); color: var(--light); margin-top: 2px; }
    .hdr-right  { flex-shrink: 0; display: flex; flex-direction: column; align-items: flex-end; gap: 6px; }
    .hdr-state  { display: inline-block; padding: 5px 14px; border-radius: 6px; background: var(--acc); color: #fff; font-weight: 700; font-size: 13px; }
    .status-badge { display: inline-block; padding: 2px 9px; border-radius: 20px; font-size: 11px; font-weight: 600; }
    .s-active    { background: var(--green-lt); color: var(--green); border: 1px solid var(--green-bd); }
    .s-completed { background: var(--acc-lt);   color: var(--acc);   border: 1px solid var(--acc-bd); }
    .s-failed    { background: var(--red-lt);   color: var(--red);   border: 1px solid var(--red-bd); }
    .s-suspended { background: var(--amber-lt); color: var(--amber); border: 1px solid var(--amber-bd); }
    .s-none      { background: #f3f4f6; color: var(--muted); border: 1px solid var(--border); }

    /* ── Meta bar ── */
    .meta-bar   { display: flex; flex-wrap: wrap; gap: 16px; margin-bottom: 10px; padding: 10px 14px; background: var(--white); border: 1px solid var(--border); border-radius: 8px; }
    .meta-item  { display: flex; flex-direction: column; gap: 1px; }
    .meta-label { font-size: 9px; text-transform: uppercase; letter-spacing: .08em; font-weight: 700; color: var(--light); }
    .meta-val   { font-size: 12px; font-weight: 600; color: var(--muted); }

    /* ── Toolbar ── */
    .toolbar    { display: flex; align-items: center; gap: 8px; margin-bottom: 10px; }
    .btn        { display: inline-flex; align-items: center; gap: 5px; padding: 4px 11px; border-radius: 5px; font-size: 11px; font-weight: 600; cursor: pointer; border: 1px solid var(--border); background: var(--white); color: var(--muted); transition: all .15s; }
    .btn:hover  { border-color: var(--acc); color: var(--acc); }
    .btn.active { background: var(--acc-lt); border-color: var(--acc); color: var(--acc); }
    .right-lbl  { font-size: 11px; color: var(--light); margin-left: auto; }

    /* ── Log table ── */
    .log-table  { width: 100%; border-collapse: collapse; background: var(--white); border: 1px solid var(--border); border-radius: 10px; overflow: hidden; }
    .log-table thead th {
      position: sticky; top: 0; z-index: 10;
      background: #f3f4f6; border-bottom: 1px solid var(--border);
      padding: 8px 12px; text-align: left;
      font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: .07em; color: var(--muted);
      white-space: nowrap;
    }
    .log-table tbody tr       { border-bottom: 1px solid var(--border); transition: background .1s; cursor: pointer; }
    .log-table tbody tr:last-child { border-bottom: none; }
    .log-table tbody tr:hover { background: #f9fafb; }
    .log-table tbody tr.expanded { background: #f0f7ff; }
    td { padding: 9px 12px; vertical-align: top; }
    .col-num   { width: 36px; text-align: center; color: var(--light); font-size: 11px; font-family: var(--mono); font-weight: 600; }
    .col-ts    { white-space: nowrap; font-size: 11px; font-family: var(--mono); color: var(--muted); min-width: 110px; }
    .col-trans { min-width: 200px; }
    .trans-states { display: flex; align-items: center; gap: 6px; }
    .st-from   { font-size: 12px; font-weight: 500; color: var(--muted); }
    .st-arrow  { color: var(--light); }
    .st-to     { font-size: 12px; font-weight: 700; color: var(--text); }
    .row-tag   { display: inline-block; font-size: 9px; font-weight: 700; padding: 1px 6px; border-radius: 3px; margin-top: 4px; }
    .rt-start  { background: var(--green-lt); color: var(--green); border: 1px solid var(--green-bd); }
    .rt-end    { background: var(--acc-lt);   color: var(--acc);   border: 1px solid var(--acc-bd); }
    .rt-reopen { background: var(--amber-lt); color: var(--amber); border: 1px solid var(--amber-bd); }
    .col-evt   { min-width: 130px; }
    .evt-name  { font-size: 12px; font-weight: 500; color: var(--text); }
    .evt-code  { font-size: 10px; font-family: var(--mono); color: var(--acc); background: var(--acc-lt); border: 1px solid var(--acc-bd); padding: 0 5px; border-radius: 3px; margin-top: 3px; display: inline-block; }
    .col-actor { min-width: 110px; font-size: 11px; font-family: var(--mono); color: var(--light); }
    .col-acts  { width: 60px; text-align: center; }
    .acts-badge { display: inline-block; padding: 2px 7px; border-radius: 12px; font-size: 10px; font-weight: 600; background: var(--acc-lt); color: var(--acc); border: 1px solid var(--acc-bd); }
    .acts-none  { color: var(--light); font-size: 11px; }
    .col-dur   { width: 60px; text-align: right; font-size: 11px; font-family: var(--mono); color: var(--muted); }
    .col-expand { width: 28px; text-align: center; }
    .exp-icon  { font-size: 14px; color: var(--light); user-select: none; transition: transform .15s; display: inline-block; }
    .exp-icon.open { transform: rotate(90deg); }

    /* ── Detail panel ── */
    .detail-row { display: none; background: #f0f7ff; }
    .detail-row.open { display: table-row; }
    .detail-cell { padding: 0 12px 12px 44px; }
    .detail-inner { border: 1px solid var(--acc-bd); border-radius: 8px; overflow: hidden; }
    .d-acts-table { width: 100%; border-collapse: collapse; }
    .d-acts-table th { background: #e8f1ff; padding: 6px 12px; text-align: left; font-size: 9px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--acc); }
    .d-acts-table td { padding: 7px 12px; font-size: 11px; border-top: 1px solid var(--acc-bd); }
    .da-name   { font-family: var(--mono); color: var(--text); }
    .da-actor  { font-family: var(--mono); color: var(--light); }
    .da-pill   { display: inline-block; padding: 2px 7px; border-radius: 10px; font-size: 10px; font-weight: 600; }
    .p-approved { background: var(--green-lt); color: var(--green); border: 1px solid var(--green-bd); }
    .p-rejected  { background: var(--red-lt);   color: var(--red);   border: 1px solid var(--red-bd); }
    .p-pending   { background: var(--amber-lt); color: var(--amber); border: 1px solid var(--amber-bd); }
    .da-dur    { text-align: right; font-family: var(--mono); color: var(--muted); }
    .no-detail { font-size: 11px; color: var(--light); font-style: italic; padding: 10px 12px; }

    /* ── Hooks in detail ── */
    .hk-hdr  { font-size: 9px; font-weight: 700; text-transform: uppercase; letter-spacing: .08em; color: var(--teal); padding: 6px 12px; background: var(--teal-lt); border-top: 1px solid var(--teal-bd); }
    .hk-tbl  { width: 100%; border-collapse: collapse; }
    .hk-tbl th { font-size: 9px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--teal); padding: 5px 12px; text-align: left; background: var(--teal-lt); }
    .hk-tbl td { padding: 6px 12px; font-size: 11px; border-top: 1px solid #ccfbf1; }
    .hk-tbl tr:hover td { background: var(--teal-lt); }
    .hk-route { font-family: var(--mono); color: var(--teal); font-weight: 600; }
    .hk-sub   { font-family: var(--mono); font-size: 10px; color: var(--light); }
    .hk-sent  { font-size: 10px; color: var(--light); }
    .hk-pill  { display: inline-block; padding: 1px 6px; border-radius: 10px; font-size: 10px; font-weight: 600; }
    .hk-ok    { background: var(--green-lt); color: var(--green); border: 1px solid var(--green-bd); }
    .hk-fail  { background: var(--red-lt);   color: var(--red);   border: 1px solid var(--red-bd); }
    .hk-pend  { background: var(--amber-lt); color: var(--amber); border: 1px solid var(--amber-bd); }
    .hk-wait  { background: #f3f4f6; color: var(--muted); border: 1px solid var(--border); }
    .hk-flag  { font-size: 9px; padding: 1px 4px; border-radius: 3px; background: var(--teal-lt); color: var(--teal); border: 1px solid var(--teal-bd); }

    /* ── Compact ── */
    .compact .hide-compact { display: none !important; }
    .compact td, .compact th { padding: 5px 10px; }
  </style>
  <script>
    var expanded = {};
    function toggleRow(idx) {
      expanded[idx] = !expanded[idx];
      var dr   = document.getElementById('dr-' + idx);
      var mr   = document.getElementById('mr-' + idx);
      var icon = document.getElementById('ei-' + idx);
      if (dr)   dr.classList.toggle('open',     expanded[idx]);
      if (mr)   mr.classList.toggle('expanded', expanded[idx]);
      if (icon) icon.classList.toggle('open',   expanded[idx]);
    }
    function collapseAll() {
      Object.keys(expanded).forEach(function(k) {
        expanded[k] = false;
        var dr   = document.getElementById('dr-' + k);
        var mr   = document.getElementById('mr-' + k);
        var icon = document.getElementById('ei-' + k);
        if (dr)   dr.classList.remove('open');
        if (mr)   mr.classList.remove('expanded');
        if (icon) icon.classList.remove('open');
      });
    }
    function toggleCompact() {
      var tbl = document.getElementById('log-table');
      var btn = document.getElementById('btn-compact');
      var on  = tbl.classList.toggle('compact');
      btn.classList.toggle('active', on);
      btn.textContent = on ? '\u229e Full' : '\u229f Compact';
    }
  </script>
""");
        if (RendererColors.TryParse(color, out var c))
            sb.Append($@"  <style>
    :root {{ --acc: {c.Base}; --acc-lt: {c.Light}; --acc-bd: {c.Border}; }}
    .log-table tbody tr.expanded {{ background: {c.Light}; }}
    .detail-row.open {{ background: {c.Light}; }}
    .d-acts-table th {{ background: {c.Light}; color: {c.Base}; }}
    .s-completed {{ background: {c.Light}; color: {c.Dark}; border-color: {c.Border}; }}
  </style>
");
        sb.Append("""
</head>
<body>
<div class="shell">

""");
    }

    // ── Header ───────────────────────────────────────────────────────────────

    private static void WriteHeader(StringBuilder sb, JsonElement inst, string? displayName) {
        var entityId = S(inst, "entity_id");
        var guid = S(inst, "guid");
        var label = !string.IsNullOrWhiteSpace(displayName) ? displayName : entityId;
        var defName = S(inst, "def_name");
        var defVer = S(inst, "def_version");
        var curState = S(inst, "current_state");
        var status = S(inst, "instance_status");
        var statusCls = status switch {
            "Active" => "s-active",
            "Completed" => "s-completed",
            "Failed" => "s-failed",
            "Suspended" => "s-suspended",
            _ => "s-none"
        };

        sb.Append($"""
  <div class="hdr">
    <div class="hdr-left">
      <div class="hdr-tag">{E(defName)} · v{E(defVer)}</div>
      <div class="hdr-entity">{E(label)}</div>
      <div class="hdr-guid">{E(entityId)}</div>
      <div class="hdr-guid" style="opacity:.7">{E(guid)}</div>
    </div>
    <div class="hdr-right">
      <div class="hdr-state">{E(curState)}</div>
      <span class="status-badge {statusCls}">{E(status)}</span>
    </div>
  </div>

""");
    }

    // ── Meta Bar ─────────────────────────────────────────────────────────────

    private static void WriteMetaBar(StringBuilder sb, JsonElement inst, int count) {
        var lastEvt = SplitCamel(S(inst, "last_event"));
        var created = FmtFull(S(inst, "created"));
        var modified = FmtFull(S(inst, "modified"));
        var totalDur = Dur(S(inst, "created"), S(inst, "modified"));

        sb.Append($"""
  <div class="meta-bar">
    <div class="meta-item"><span class="meta-label">Last Event</span><span class="meta-val">{E(lastEvt)}</span></div>
    <div class="meta-item"><span class="meta-label">Started</span><span class="meta-val">{E(created)}</span></div>
    <div class="meta-item"><span class="meta-label">Ended</span><span class="meta-val">{E(modified)}</span></div>
    <div class="meta-item"><span class="meta-label">Total</span><span class="meta-val">{E(totalDur)}</span></div>
    <div class="meta-item"><span class="meta-label">Transitions</span><span class="meta-val">{count}</span></div>
  </div>

  <div class="toolbar">
    <button class="btn" id="btn-compact" onclick="toggleCompact()">⊟ Compact</button>
    <button class="btn" onclick="collapseAll()">− Collapse All</button>
    <span class="right-lbl">{count} transition{(count != 1 ? "s" : "")}</span>
  </div>

""");
    }

    // ── Table ─────────────────────────────────────────────────────────────────

    private static void WriteTable(StringBuilder sb, List<JsonElement> items, TimelineDetail detail) {
        sb.Append("""
  <div class="scroll">
    <table class="log-table" id="log-table">
      <thead>
        <tr>
          <th class="col-num">#</th>
          <th>Timestamp</th>
          <th>Transition</th>
          <th>Event</th>
          <th class="hide-compact">Actor</th>
          <th class="hide-compact col-acts">Items</th>
          <th style="text-align:right">Gap</th>
          <th class="col-expand"></th>
        </tr>
      </thead>
      <tbody>
""");
        var prevTerminal = false;
        for (var i = 0; i < items.Count; i++) {
            WriteRow(sb, items[i], i, items.Count,
                i + 1 < items.Count ? (JsonElement?)items[i + 1] : null,
                prevTerminal, detail);
            prevTerminal = B(items[i], "is_terminal");
        }
        sb.Append("""
      </tbody>
    </table>
  </div>

""");
    }

    private static void WriteRow(StringBuilder sb, JsonElement item, int idx, int total, JsonElement? next, bool prevTerminal, TimelineDetail detail) {
        var fromState = S(item, "from_state");
        var toState = S(item, "to_state");
        var evtName = SplitCamel(S(item, "event"));
        var evtCode = S(item, "event_code");
        var actor = S(item, "actor");
        var created = Fmt(S(item, "created"));
        var isInitial = B(item, "is_initial");
        var isTerminal = B(item, "is_terminal");
        var isReopen = idx > 0 && prevTerminal;

        item.TryGetProperty("activities", out var acts);
        item.TryGetProperty("hooks", out var hooks);
        var actCount = acts.ValueKind == JsonValueKind.Array ? acts.GetArrayLength() : 0;
        var hookCount = CountVisibleHooks(hooks);

        var hasRejected = acts.ValueKind == JsonValueKind.Array &&
            acts.EnumerateArray().Any(a => string.Equals(S(a, "status"), "rejected", StringComparison.OrdinalIgnoreCase));

        var gap = next.HasValue ? Dur(S(item, "created"), S(next.Value, "created")) : "—";

        var tag = isInitial ? """<div><span class="row-tag rt-start">Started</span></div>"""
                : isTerminal ? """<div><span class="row-tag rt-end">Ended</span></div>"""
                : isReopen ? """<div><span class="row-tag rt-reopen">Reopened</span></div>"""
                : string.Empty;

        var itemCount = actCount + hookCount;
        var itemBadge = itemCount > 0
            ? $"""<span class="acts-badge">{actCount}a{(hookCount > 0 ? $"+{hookCount}h" : "")}</span>"""
            : """<span class="acts-none">—</span>""";

        var rowStyle = hasRejected ? " style=\"background:#fff5f5\"" : string.Empty;

        sb.Append($"""
        <tr id="mr-{idx}" onclick="toggleRow({idx})"{rowStyle}>
          <td class="col-num">{idx + 1}</td>
          <td class="col-ts">{E(created)}</td>
          <td class="col-trans">
            <div class="trans-states">
              <span class="st-from">{E(fromState)}</span>
              <span class="st-arrow">→</span>
              <span class="st-to">{E(toState)}</span>
            </div>
            {tag}
          </td>
          <td class="col-evt">
            <div class="evt-name">{E(evtName)}</div>
            <span class="evt-code">evt:{E(evtCode)}</span>
          </td>
          <td class="col-actor hide-compact">{E(actor)}</td>
          <td class="col-acts hide-compact">{itemBadge}</td>
          <td class="col-dur">{E(gap)}</td>
          <td class="col-expand"><span class="exp-icon" id="ei-{idx}">›</span></td>
        </tr>
        <tr class="detail-row" id="dr-{idx}">
          {DetailCell(acts, hooks, detail)}
        </tr>
""");
    }

    private static string DetailCell(JsonElement acts, JsonElement hooks, TimelineDetail detail) {
        var hasActs = acts.ValueKind == JsonValueKind.Array && acts.GetArrayLength() > 0;
        var hasHooks = hooks.ValueKind == JsonValueKind.Array && CountVisibleHooks(hooks) > 0
                       && detail >= TimelineDetail.Admin;

        if (!hasActs && !hasHooks)
            return """<td colspan="8" class="detail-cell"><p class="no-detail">No activities or hooks recorded.</p></td>""";

        var sb = new StringBuilder();
        sb.Append("""<td colspan="8" class="detail-cell"><div class="detail-inner">""");

        if (hasActs) {
            sb.Append("""
          <table class="d-acts-table">
            <thead><tr><th>Activity</th><th>Actor</th><th>Status</th><th style="text-align:right">Duration</th></tr></thead>
            <tbody>
""");
            foreach (var a in acts.EnumerateArray()) {
                var activity = S(a, "activity");
                var label = S(a, "label");
                var display = !string.IsNullOrWhiteSpace(label) ? label : activity;
                var actorId = S(a, "actor_id");
                var status = S(a, "status");
                var dur = Dur(S(a, "created"), S(a, "modified"));
                var pillCls = status.ToLowerInvariant() switch {
                    "approved" => "p-approved",
                    "rejected" => "p-rejected",
                    _ => "p-pending"
                };
                sb.Append($"""
              <tr>
                <td><div class="da-name">{E(display)}</div><div class="da-actor">{E(actorId)}</div></td>
                <td></td>
                <td><span class="da-pill {pillCls}">{E(status)}</span></td>
                <td class="da-dur">{E(dur)}</td>
              </tr>
""");
            }
            sb.Append("""
            </tbody>
          </table>
""");
        }

        if (hasHooks) {
            sb.Append("""
          <div class="hk-hdr">Hooks / Emits</div>
          <table class="hk-tbl">
            <thead><tr><th>#</th><th>Route</th><th>Status</th><th>Flags</th></tr></thead>
            <tbody>
""");
            foreach (var h in hooks.EnumerateArray()) {
                if (!IsMainTimelineHookVisible(h)) continue;

                var route = S(h, "route");
                var label = S(h, "label");
                var display = !string.IsNullOrWhiteSpace(label) ? label : route;
                var orderSeq = S(h, "order_seq");
                var isGate = h.TryGetProperty("hook_type", out var htv) && htv.TryGetInt32(out var htInt) ? htInt == 1 : true;
                var onEntry = B(h, "on_entry");
                var dispatched = B(h, "dispatched");
                var rawTrigger = S(h, "last_trigger");
                var lastSent = Fmt(rawTrigger);
                var total = h.TryGetProperty("total_acks", out var tv) ? tv.GetInt32() : 0;
                var processed = h.TryGetProperty("processed_acks", out var pv) ? pv.GetInt32() : 0;
                var failed = h.TryGetProperty("failed_acks", out var fv) ? fv.GetInt32() : 0;
                var retries = h.TryGetProperty("max_retries", out var rv) ? rv.GetInt32() : 0;
                var totalSent = h.TryGetProperty("total_triggers", out var ttv) ? ttv.GetInt32() : 0;

                var pillCls = !dispatched ? "hk-wait"
                            : failed > 0 ? "hk-fail"
                            : total > 0 && processed == total ? "hk-ok"
                            : "hk-pend";
                var pillLbl = !dispatched ? "Not Dispatched"
                            : failed > 0 ? $"Failed {failed}/{total}"
                            : total > 0 ? $"ACKed {processed}/{total}"
                            : "Dispatched";

                var subHtml = !string.IsNullOrWhiteSpace(label) ? $"""<div class="hk-sub">{E(route)}</div>""" : string.Empty;
                var sentHtml = !string.IsNullOrWhiteSpace(rawTrigger) ? $"""<div class="hk-sent">Last sent: {E(lastSent)}</div>""" : string.Empty;

                var flags = new StringBuilder();
                flags.Append(isGate ? """<span class="hk-flag">gate</span> """ : """<span class="hk-flag" style="background:#f0fdf4;color:#15803d;border-color:#bbf7d0">effect</span> """);
                if (!onEntry) flags.Append("""<span class="hk-flag" style="background:#fffbeb;color:#92400e;border-color:#fde68a">on-exit</span> """);
                if (totalSent > 0) flags.Append($"""<span class="hk-flag">{totalSent}× sent</span> """);
                if (retries > 0) flags.Append($"""<span class="hk-flag" style="background:#fdf2f8;color:#9d174d;border-color:#fbcfe8">{retries} retr.</span>""");

                sb.Append($"""
              <tr>
                <td style="color:var(--light);font-family:var(--mono);text-align:center">{E(orderSeq)}</td>
                <td><div class="hk-route">{E(display)}</div>{subHtml}{sentHtml}</td>
                <td><span class="hk-pill {pillCls}">{E(pillLbl)}</span></td>
                <td>{flags}</td>
              </tr>
""");
            }
            sb.Append("""
            </tbody>
          </table>
""");
        }

        sb.Append("</div></td>");
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string S(JsonElement el, string key) {
        if (!el.TryGetProperty(key, out var v)) return string.Empty;
        return v.ValueKind switch {
            JsonValueKind.String => v.GetString() ?? string.Empty,
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool B(JsonElement el, string key) {
        if (!el.TryGetProperty(key, out var v)) return false;
        return v.ValueKind == JsonValueKind.True;
    }

    private static string BuildPageTitle(JsonElement root) {
        if (!root.TryGetProperty("instance", out var inst)) return "WFE-TL-UNKNOWN";
        var guid = S(inst, "guid");
        return string.IsNullOrWhiteSpace(guid) ? "WFE-TL-UNKNOWN" : $"WFE-TL-{guid}";
    }

    private static int CountVisibleHooks(JsonElement hooks) {
        if (hooks.ValueKind != JsonValueKind.Array) return 0;
        var total = 0;
        foreach (var hook in hooks.EnumerateArray()) {
            if (IsMainTimelineHookVisible(hook)) total++;
        }
        return total;
    }

    private static bool IsMainTimelineHookVisible(JsonElement hook) =>
        B(hook, "dispatched") && HookStatusInt(hook) != 2;

    private static int HookStatusInt(JsonElement hook) {
        if (!hook.TryGetProperty("hook_status", out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)) return parsed;
        return int.TryParse(value.ToString(), out var fallback) ? fallback : 0;
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string SplitCamel(string s) {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length + 6);
        for (var i = 0; i < s.Length; i++) {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string FmtFull(string dt) =>
        TryParse(dt, out var d)
            ? d.ToLocalTime().ToString("MMM d, yyyy h:mm:ss tt", CultureInfo.InvariantCulture)
            : dt;

    private static string Fmt(string dt) =>
        TryParse(dt, out var d)
            ? d.ToLocalTime().ToString("MMM d, h:mm:ss tt", CultureInfo.InvariantCulture)
            : dt;

    private static string Dur(string a, string b) {
        if (!TryParse(a, out var da) || !TryParse(b, out var db)) return "?";
        var m = (int)Math.Round((db - da).TotalMinutes);
        return m < 1 ? "<1m" : m < 60 ? $"{m}m" : $"{m / 60}h {m % 60}m";
    }

    private static bool TryParse(string dt, out DateTime result) =>
        DateTime.TryParse(dt, null,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
}
