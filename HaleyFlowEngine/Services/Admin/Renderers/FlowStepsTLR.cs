using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Haley.Enums;

namespace Haley.Services;

/// <summary>
/// Converts a timeline JSON string into a self-contained HTML page.
/// Design: Flow Steps (B) — horizontal progress rail, colored step-number strips, table-style activities.
/// </summary>
internal static class FlowStepsTLR {

    public static string Render(string timelineJson, string? displayName = null, TimelineDetail detail = TimelineDetail.Detailed, string? color = null) {
        using var doc = JsonDocument.Parse(timelineJson);
        var root = doc.RootElement;
        var pageTitle = BuildPageTitle(root);

        var sb = new StringBuilder(20_000);
        WriteHead(sb, pageTitle, color);
        sb.Append("<div class=\"shell\">\n\n");

        if (root.TryGetProperty("instance", out var inst) &&
            root.TryGetProperty("timeline", out var tl) &&
            tl.ValueKind == JsonValueKind.Array) {

            var items    = tl.EnumerateArray().ToList();
            var count    = items.Count;
            var totalDur = count > 0 ? Dur(S(inst, "created"), S(inst, "modified")) : "—";

            WriteHeader(sb, inst, displayName, count, totalDur);
            WriteProgress(sb, items);

            sb.Append($"""
  <div class="controls">
    <button class="btn" id="btn-compact" onclick="toggleCompact()">⊟ Compact</button>
    <span class="count-lbl">{count} transition{(count != 1 ? "s" : "")}  ·  {E(totalDur)} total</span>
  </div>

  <div class="scroll">
    <div class="steps" id="steps-area">

""");
            var prevTerminal = false;
            for (var i = 0; i < items.Count; i++) {
                WriteStep(sb, items[i], i, items.Count,
                    i + 1 < items.Count ? (JsonElement?)items[i + 1] : null,
                    prevTerminal, detail);
                prevTerminal = B(items[i], "is_terminal");
            }

            sb.Append("""
    </div>
  </div>

""");
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
      --bg:        #f5f7fa;
      --white:     #ffffff;
      --border:    #e2e8f0;
      --text:      #1e293b;
      --muted:     #64748b;
      --light:     #94a3b8;
      --blue:      #2563eb;
      --blue-lt:   #eff6ff;
      --blue-bd:   #bfdbfe;
      --green:     #16a34a;
      --green-lt:  #f0fdf4;
      --green-bd:  #bbf7d0;
      --amber:     #d97706;
      --amber-lt:  #fffbeb;
      --amber-bd:  #fde68a;
      --red:       #dc2626;
      --red-lt:    #fef2f2;
      --red-bd:    #fecaca;
      --purple:    #7c3aed;
      --purple-lt: #f5f3ff;
      --purple-bd: #ddd6fe;
      --slate:     #475569;
    }
    html, body { height: 100%; background: var(--bg); color: var(--text); font-family: -apple-system, 'Segoe UI', sans-serif; font-size: 14px; }
    body { display: flex; flex-direction: column; overflow: hidden; }
    ::-webkit-scrollbar { width: 5px; }
    ::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 3px; }

    .shell  { max-width: 860px; width: 100%; margin: 0 auto; padding: 24px 16px 0; height: 100vh; display: flex; flex-direction: column; }
    .scroll { flex: 1; overflow-y: auto; padding-bottom: 36px; }

    /* ── Header ── */
    .hdr        { background: var(--white); border: 1px solid var(--border); border-radius: 14px; margin-bottom: 16px; overflow: hidden; }
    .hdr-body   { padding: 20px 24px; display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
    .hdr-def    { font-size: 11px; text-transform: uppercase; letter-spacing: .1em; color: var(--blue); font-weight: 700; margin-bottom: 5px; }
    .hdr-entity { font-size: 20px; font-weight: 800; color: var(--text); }
    .hdr-guid   { font-size: 11px; font-family: monospace; color: var(--light); margin-top: 3px; }
    .hdr-state  { text-align: right; flex-shrink: 0; }
    .hdr-state-label { font-size: 10px; text-transform: uppercase; letter-spacing: .08em; color: var(--muted); margin-bottom: 5px; }
    .state-chip { display: inline-block; padding: 7px 16px; border-radius: 999px; background: var(--blue); color: #fff; font-weight: 700; font-size: 13px; }
    .hdr-footer { padding: 12px 24px; background: #f8fafc; border-top: 1px solid var(--border); display: flex; flex-wrap: wrap; gap: 20px; }
    .hdr-stat   { display: flex; flex-direction: column; gap: 2px; }
    .hdr-stat-label { font-size: 10px; text-transform: uppercase; letter-spacing: .08em; color: var(--light); font-weight: 600; }
    .hdr-stat-val   { font-size: 12px; color: var(--slate); font-weight: 600; }
    .status-badge { display: inline-block; padding: 2px 9px; border-radius: 20px; font-size: 11px; font-weight: 600; }
    .s-active    { background: var(--green-lt); color: var(--green); border: 1px solid var(--green-bd); }
    .s-completed { background: var(--blue-lt);  color: var(--blue);  border: 1px solid var(--blue-bd); }
    .s-failed    { background: var(--red-lt);   color: var(--red);   border: 1px solid var(--red-bd); }
    .s-suspended { background: var(--amber-lt); color: var(--amber); border: 1px solid var(--amber-bd); }
    .s-none      { background: #f1f5f9; color: var(--muted); border: 1px solid var(--border); }

    /* ── Progress Rail ── */
    .progress-wrap  { background: var(--white); border: 1px solid var(--border); border-radius: 12px; padding: 10px 20px; margin-bottom: 16px; }
    .progress-header { display: flex; align-items: center; justify-content: space-between; cursor: pointer; user-select: none; }
    .progress-label { font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .08em; color: var(--muted); }
    .progress-toggle { font-size: 11px; color: var(--muted); background: none; border: none; cursor: pointer; padding: 0 2px; line-height: 1; }
    .progress-rail  { display: flex; align-items: center; overflow-x: auto; padding-bottom: 4px; margin-top: 12px; }
    .progress-rail::-webkit-scrollbar { height: 3px; }
    .progress-rail::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 2px; }
    .progress-rail.collapsed { display: none; }
    .prog-step  { display: flex; flex-direction: column; align-items: center; flex-shrink: 0; cursor: pointer; }
    .prog-node  { width: 34px; height: 34px; border-radius: 50%; border: 2px solid var(--border); display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 700; background: var(--white); color: var(--muted); transition: all .2s; position: relative; z-index: 1; }
    .prog-node.visited  { background: var(--blue);  border-color: var(--blue);  color: #fff; }
    .prog-node.terminal { background: var(--green); border-color: var(--green); color: #fff; }
    .prog-node.reentry  { background: var(--amber); border-color: var(--amber); color: #fff; }
    .prog-node:hover    { transform: scale(1.1); box-shadow: 0 2px 8px rgba(37,99,235,.25); }
    .prog-name      { font-size: 9px; color: var(--muted); margin-top: 5px; text-align: center; max-width: 60px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .prog-connector { flex: 1; min-width: 16px; max-width: 40px; height: 2px; background: var(--blue); margin-bottom: 20px; flex-shrink: 0; }

    /* ── Controls ── */
    .controls  { display: flex; align-items: center; gap: 8px; margin-bottom: 14px; }
    .btn       { display: inline-flex; align-items: center; gap: 5px; padding: 5px 13px; border-radius: 6px; font-size: 12px; font-weight: 600; cursor: pointer; border: 1px solid var(--border); background: var(--white); color: var(--muted); transition: all .15s; }
    .btn:hover { border-color: var(--blue); color: var(--blue); }
    .btn.active { background: var(--blue-lt); border-color: var(--blue); color: var(--blue); }
    .count-lbl { font-size: 11px; color: var(--light); margin-left: auto; }

    /* ── Steps ── */
    .steps { display: flex; flex-direction: column; gap: 8px; }
    .step  { background: var(--white); border: 1px solid var(--border); border-radius: 12px; overflow: hidden; transition: box-shadow .15s; }
    .step:hover { box-shadow: 0 2px 12px rgba(0,0,0,.06); }

    .step-hdr        { display: flex; align-items: stretch; }
    .step-num        { width: 42px; flex-shrink: 0; display: flex; align-items: center; justify-content: center; font-size: 12px; font-weight: 800; color: #fff; background: var(--blue); }
    .step-num.s-start  { background: var(--green); }
    .step-num.s-end    { background: #1d4ed8; }
    .step-num.s-reopen { background: var(--amber); }
    .step-transition { flex: 1; display: flex; align-items: center; gap: 10px; padding: 12px 16px; flex-wrap: wrap; min-width: 0; }
    .from-chip   { padding: 4px 10px; border-radius: 6px; font-size: 12px; font-weight: 600; background: #f1f5f9; color: var(--slate); }
    .to-chip     { padding: 4px 10px; border-radius: 6px; font-size: 12px; font-weight: 600; background: var(--blue-lt); color: var(--blue); }
    .step-arrow  { color: var(--light); font-size: 18px; }
    .special-tag { font-size: 10px; font-weight: 700; padding: 2px 7px; border-radius: 4px; }
    .tag-start   { background: var(--green-lt); color: var(--green); border: 1px solid var(--green-bd); }
    .tag-end     { background: var(--blue-lt);  color: var(--blue);  border: 1px solid var(--blue-bd); }
    .tag-reopen  { background: var(--amber-lt); color: var(--amber); border: 1px solid var(--amber-bd); }
    .step-right  { display: flex; flex-direction: column; align-items: flex-end; justify-content: center; padding: 12px 16px 12px 8px; flex-shrink: 0; gap: 3px; }
    .step-time   { font-size: 11px; color: var(--light); }
    .step-acts-count { font-size: 11px; color: var(--blue); font-weight: 600; }

    .step-body  { border-top: 1px solid var(--border); }
    .step-meta  { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; padding: 9px 16px; background: #f8fafc; border-bottom: 1px solid #f1f5f9; }
    .event-name { font-size: 12px; font-weight: 600; color: var(--text); }
    .evt-code   { font-size: 10px; font-family: monospace; padding: 1px 6px; border-radius: 4px; background: var(--blue-lt); color: var(--blue); border: 1px solid var(--blue-bd); }
    .lc-code    { font-size: 10px; font-family: monospace; padding: 1px 6px; border-radius: 4px; background: #f1f5f9; color: var(--muted); border: 1px solid var(--border); }
    .meta-actor { font-size: 11px; font-family: monospace; color: var(--light); margin-left: auto; }

    /* ── Activities table ── */
    .acts-table { width: 100%; border-collapse: collapse; }
    .acts-table th { font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--light); padding: 7px 14px; text-align: left; background: #f8fafc; }
    .acts-table td { padding: 8px 14px; font-size: 12px; border-top: 1px solid #f1f5f9; }
    .acts-table tr:hover td { background: #f8fafc; }
    .act-name  { font-family: monospace; color: var(--text); font-weight: 500; }
    .act-actor { font-family: monospace; font-size: 11px; color: var(--light); }
    .act-pill  { display: inline-block; padding: 2px 8px; border-radius: 12px; font-size: 10px; font-weight: 600; }
    .pill-approved { background: var(--green-lt); color: var(--green); border: 1px solid var(--green-bd); }
    .pill-rejected  { background: var(--red-lt);   color: var(--red);   border: 1px solid var(--red-bd); }
    .pill-pending   { background: var(--amber-lt); color: var(--amber); border: 1px solid var(--amber-bd); }
    .act-dur   { font-size: 11px; color: var(--light); text-align: right; }
    .no-acts   { font-size: 11px; color: var(--light); font-style: italic; padding: 10px 14px; }

    /* ── Hooks table ── */
    .hooks-table { width: 100%; border-collapse: collapse; }
    .hooks-table thead th { font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--purple); padding: 6px 14px; text-align: left; background: var(--purple-lt); border-top: 2px solid var(--purple-bd); }
    .hooks-table td { padding: 7px 14px; font-size: 11px; border-top: 1px solid var(--purple-bd); }
    .hooks-table tr:hover td { background: var(--purple-lt); }
    .hk-name  { font-family: monospace; color: var(--slate); font-weight: 600; }
    .hk-sub   { font-family: monospace; font-size: 10px; color: var(--light); }
    .hk-sent  { font-size: 10px; color: var(--light); }
    .hk-pill  { display: inline-block; padding: 2px 7px; border-radius: 12px; font-size: 10px; font-weight: 600; }
    .hk-ok    { background: var(--green-lt); color: var(--green); border: 1px solid var(--green-bd); }
    .hk-fail  { background: var(--red-lt);   color: var(--red);   border: 1px solid var(--red-bd); }
    .hk-pend  { background: var(--amber-lt); color: var(--amber); border: 1px solid var(--amber-bd); }
    .hk-wait  { background: #f1f5f9; color: var(--muted); border: 1px solid var(--border); }
    .hk-badge { font-size: 9px; padding: 1px 5px; border-radius: 4px; background: var(--purple-lt); color: var(--purple); border: 1px solid var(--purple-bd); }
    .hk-seq   { font-size: 9px; font-weight: 700; color: var(--purple); margin-right: 5px; }

    /* ── Gap ── */
    .step-gap      { display: flex; align-items: center; gap: 8px; padding: 2px 16px 2px 56px; }
    .step-gap-line { height: 1px; flex: 1; max-width: 60px; background: var(--border); }
    .step-gap-text { font-size: 10px; color: var(--light); }

    /* ── Compact ── */
    .compact .detail-only { display: none !important; }
    .compact .step.force-open .detail-only { display: block !important; }
    .compact .step { cursor: pointer; }
    .compact .step.force-open { box-shadow: 0 2px 12px rgba(37,99,235,.12); }
    .compact-row { display: none; align-items: center; gap: 8px; padding: 9px 14px; }
    .compact .compact-row { display: flex; }
    .cr-states { display: flex; align-items: center; gap: 7px; flex: 1; flex-wrap: wrap; }
    .cr-from   { font-size: 12px; font-weight: 600; color: var(--muted); }
    .cr-to     { font-size: 12px; font-weight: 700; color: var(--blue); }
    .cr-evt    { font-size: 11px; color: var(--light); flex: 1; }
    .cr-ts     { font-size: 11px; color: var(--light); font-family: monospace; flex-shrink: 0; }
  </style>
  <script>
    function toggleCompact() {
      var area = document.getElementById('steps-area');
      var btn  = document.getElementById('btn-compact');
      var on   = area.classList.toggle('compact');
      btn.classList.toggle('active', on);
      btn.textContent = on ? '\u229e Expanded' : '\u229f Compact';
      if (!on) area.querySelectorAll('.step.force-open').forEach(function(s) { s.classList.remove('force-open'); });
    }
    function toggleFlowStep(idx) {
      var area = document.getElementById('steps-area');
      if (!area || !area.classList.contains('compact')) return;
      var step = document.getElementById('step-' + idx);
      if (step) step.classList.toggle('force-open');
    }
    function scrollToStep(idx) {
      var el = document.getElementById('step-' + idx);
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
    function toggleJourney() {
      var rail   = document.getElementById('journey-rail');
      var toggle = document.getElementById('journey-toggle');
      var collapsed = rail.classList.toggle('collapsed');
      toggle.textContent = collapsed ? '▼' : '▲';
      toggle.title = collapsed ? 'Expand' : 'Collapse';
    }
  </script>
""");
        if (RendererColors.TryParse(color, out var c))
            sb.Append($@"  <style>
    :root {{ --blue: {c.Base}; --blue-lt: {c.Light}; --blue-bd: {c.Border}; }}
    .prog-node:hover {{ box-shadow: 0 2px 8px rgba({c.R},{c.G},{c.B},.25); }}
    .s-completed {{ background: {c.Light}; color: {c.Dark}; border-color: {c.Border}; }}
  </style>
");
        sb.Append("""
</head>
<body>
""");
    }

    // ── Header ───────────────────────────────────────────────────────────────

    private static void WriteHeader(StringBuilder sb, JsonElement inst, string? displayName, int count, string totalDur) {
        var entityId = S(inst, "entity_id");
        var guid     = S(inst, "guid");
        var label    = !string.IsNullOrWhiteSpace(displayName) ? displayName : entityId;
        var defName  = S(inst, "def_name");
        var defVer   = S(inst, "def_version");
        var curState = S(inst, "current_state");
        var lastEvt  = SplitCamel(S(inst, "last_event"));
        var created  = FmtFull(S(inst, "created"));
        var modified = FmtFull(S(inst, "modified"));
        var status   = S(inst, "instance_status");
        var statusCls = status switch {
            "Active"    => "s-active",
            "Completed" => "s-completed",
            "Failed"    => "s-failed",
            "Suspended" => "s-suspended",
            _           => "s-none"
        };

        sb.Append($"""
  <div class="hdr">
    <div class="hdr-body">
      <div style="min-width:0">
        <div class="hdr-def">{E(defName)} · v{E(defVer)}</div>
        <div class="hdr-entity">{E(label)}</div>
        <div class="hdr-guid">{E(entityId)}</div>
        <div class="hdr-guid" style="opacity:.7">{E(guid)}</div>
      </div>
      <div class="hdr-state">
        <div class="hdr-state-label">Current State</div>
        <div class="state-chip">{E(curState)}</div>
      </div>
    </div>
    <div class="hdr-footer">
      <div class="hdr-stat"><span class="hdr-stat-label">Status</span><span class="hdr-stat-val"><span class="status-badge {statusCls}">{E(status)}</span></span></div>
      <div class="hdr-stat"><span class="hdr-stat-label">Last Event</span><span class="hdr-stat-val">{E(lastEvt)}</span></div>
      <div class="hdr-stat"><span class="hdr-stat-label">Started</span><span class="hdr-stat-val">{E(created)}</span></div>
      <div class="hdr-stat"><span class="hdr-stat-label">Ended</span><span class="hdr-stat-val">{E(modified)}</span></div>
      <div class="hdr-stat"><span class="hdr-stat-label">Duration</span><span class="hdr-stat-val">{E(totalDur)}</span></div>
      <div class="hdr-stat"><span class="hdr-stat-label">Transitions</span><span class="hdr-stat-val">{count}</span></div>
    </div>
  </div>

""");
    }

    // ── Progress Rail ─────────────────────────────────────────────────────────

    private static void WriteProgress(StringBuilder sb, List<JsonElement> items) {
        sb.Append("""
  <div class="progress-wrap">
    <div class="progress-header" onclick="toggleJourney()">
      <span class="progress-label">State Journey</span>
      <button class="progress-toggle" id="journey-toggle" title="Collapse">▲</button>
    </div>
    <div class="progress-rail" id="journey-rail">
""");
        var prevTerminal = false;
        for (var i = 0; i < items.Count; i++) {
            var isTerminal = B(items[i], "is_terminal");
            var isReopen   = i > 0 && prevTerminal;
            var nodeCls    = isTerminal ? "terminal" : isReopen ? "reentry" : "visited";
            sb.Append($"""
      <div class="prog-step" onclick="scrollToStep({i})">
        <div class="prog-node {nodeCls}">{i + 1}</div>
        <div class="prog-name">{E(S(items[i], "to_state"))}</div>
      </div>
""");
            if (i < items.Count - 1) sb.Append("      <div class=\"prog-connector\"></div>\n");
            prevTerminal = isTerminal;
        }
        sb.Append("""
    </div>
  </div>

""");
    }

    // ── Step ─────────────────────────────────────────────────────────────────

    private static void WriteStep(StringBuilder sb, JsonElement item, int idx, int total, JsonElement? next, bool prevTerminal, TimelineDetail detail) {
        var fromState  = S(item, "from_state");
        var toState    = S(item, "to_state");
        var evtName    = SplitCamel(S(item, "event"));
        var evtCode    = S(item, "event_code");
        var lcId       = S(item, "lifecycle_id");
        var actor      = S(item, "actor");
        var created    = Fmt(S(item, "created"));
        var isInitial  = B(item, "is_initial");
        var isTerminal = B(item, "is_terminal");
        var isReopen   = idx > 0 && prevTerminal;
        var isLast     = idx == total - 1;

        item.TryGetProperty("activities", out var acts);
        item.TryGetProperty("hooks",      out var hooks);
        var actCount  = acts.ValueKind  == JsonValueKind.Array ? acts.GetArrayLength()  : 0;
        var hookCount = hooks.ValueKind == JsonValueKind.Array ? hooks.GetArrayLength() : 0;

        var numCls = isInitial ? "s-start" : isTerminal ? "s-end" : isReopen ? "s-reopen" : "";
        var tag = (isInitial, isTerminal, isReopen) switch {
            (true, _, _) => """<span class="special-tag tag-start">Started</span>""",
            (_, true, _) => """<span class="special-tag tag-end">Completed</span>""",
            (_, _, true) => """<span class="special-tag tag-reopen">Reopened</span>""",
            _            => string.Empty
        };

        var countHtml = actCount > 0 || hookCount > 0
            ? $"""<span class="step-acts-count">{(actCount > 0 ? $"{actCount} act." : "")}{(actCount > 0 && hookCount > 0 ? " · " : "")}{(hookCount > 0 ? $"{hookCount} hook{(hookCount != 1 ? "s" : "")}" : "")}</span>"""
            : string.Empty;

        sb.Append($"""
  <div class="step" id="step-{idx}">
    <div class="step-hdr" onclick="toggleFlowStep({idx})">
      <div class="step-num {numCls}">{idx + 1}</div>
      <div class="step-transition">
        <span class="from-chip">{E(fromState)}</span>
        <span class="step-arrow">→</span>
        <span class="to-chip">{E(toState)}</span>
        {tag}
      </div>
      <div class="step-right">
        <span class="step-time">{E(created)}</span>
        {countHtml}
      </div>
    </div>
    <div class="compact-row">
      <div class="cr-states">
        <span class="cr-from">{E(fromState)}</span>
        <span style="color:var(--light)">→</span>
        <span class="cr-to">{E(toState)}</span>
      </div>
      <span class="cr-evt">{E(evtName)}</span>
      <span class="cr-ts">{E(created)}</span>
      {tag}
    </div>
    <div class="step-body detail-only">
      <div class="step-meta">
        <span class="event-name">{E(evtName)}</span>
        <span class="evt-code">evt:{E(evtCode)}</span>
        <span class="lc-code">lc:{E(lcId)}</span>
        <span class="meta-actor">{E(actor)}</span>
      </div>
""");

        WriteActivities(sb, acts);
        if (detail >= TimelineDetail.Admin) WriteHooks(sb, hooks);

        sb.Append("""
    </div>
  </div>

""");

        if (!isLast && next.HasValue) {
            var gap = Dur(S(item, "created"), S(next.Value, "created"));
            sb.Append($"""
  <div class="step-gap detail-only">
    <div class="step-gap-line"></div>
    <div class="step-gap-text">{E(gap)} to next</div>
  </div>

""");
        }
    }

    // ── Activities table ──────────────────────────────────────────────────────

    private static void WriteActivities(StringBuilder sb, JsonElement acts) {
        if (acts.ValueKind != JsonValueKind.Array || acts.GetArrayLength() == 0) {
            sb.Append("      <p class=\"no-acts\">No activities recorded.</p>\n");
            return;
        }

        sb.Append("""
      <table class="acts-table">
        <thead><tr><th>Activity</th><th>Actor</th><th>Status</th><th style="text-align:right">Duration</th></tr></thead>
        <tbody>
""");
        foreach (var a in acts.EnumerateArray()) {
            var activity = S(a, "activity");
            var label    = S(a, "label");
            var display  = !string.IsNullOrWhiteSpace(label) ? label : activity;
            var actorId  = S(a, "actor_id");
            var status   = S(a, "status");
            var dur      = Dur(S(a, "created"), S(a, "modified"));
            var pillCls  = status.ToLowerInvariant() switch {
                "approved" => "pill-approved",
                "rejected" => "pill-rejected",
                _          => "pill-pending"
            };
            sb.Append($"""
          <tr>
            <td><div class="act-name">{E(display)}</div><div class="act-actor">{E(actorId)}</div></td>
            <td></td>
            <td><span class="act-pill {pillCls}">{E(status)}</span></td>
            <td class="act-dur">{E(dur)}</td>
          </tr>
""");
        }
        sb.Append("""
        </tbody>
      </table>
""");
    }

    // ── Hooks table ───────────────────────────────────────────────────────────

    private static void WriteHooks(StringBuilder sb, JsonElement hooks) {
        if (hooks.ValueKind != JsonValueKind.Array || hooks.GetArrayLength() == 0) return;

        sb.Append("""
      <table class="hooks-table">
        <thead><tr><th>Hook / Emit</th><th>Status</th><th>Flags</th></tr></thead>
        <tbody>
""");
        foreach (var h in hooks.EnumerateArray()) {
            var route      = S(h, "route");
            var label      = S(h, "label");
            var display    = !string.IsNullOrWhiteSpace(label) ? label : route;
            var orderSeq   = S(h, "order_seq");
            var isGate     = h.TryGetProperty("hook_type", out var htv) && htv.TryGetInt32(out var htInt) ? htInt == 1 : true;
            var onEntry    = B(h, "on_entry");
            var dispatched = B(h, "dispatched");
            var rawTrigger = S(h, "last_trigger");
            var lastSent   = Fmt(rawTrigger);
            var total      = h.TryGetProperty("total_acks",     out var tv) ? tv.GetInt32() : 0;
            var processed  = h.TryGetProperty("processed_acks", out var pv) ? pv.GetInt32() : 0;
            var failed     = h.TryGetProperty("failed_acks",    out var fv) ? fv.GetInt32() : 0;
            var retries    = h.TryGetProperty("max_retries",    out var rv) ? rv.GetInt32() : 0;
            var totalSent  = h.TryGetProperty("total_triggers", out var ttv) ? ttv.GetInt32() : 0;

            var pillCls = !dispatched ? "hk-wait"
                        : failed > 0  ? "hk-fail"
                        : total > 0 && processed == total ? "hk-ok"
                        : "hk-pend";
            var pillLbl = !dispatched ? "Not Dispatched"
                        : failed > 0  ? $"Failed {failed}/{total}"
                        : total > 0   ? $"ACKed {processed}/{total}"
                        : "Dispatched";

            var subHtml  = !string.IsNullOrWhiteSpace(label) ? $"""<div class="hk-sub">{E(route)}</div>""" : string.Empty;
            var sentHtml = !string.IsNullOrWhiteSpace(rawTrigger) ? $"""<div class="hk-sent">Sent: {E(lastSent)}</div>""" : string.Empty;

            var badges = new StringBuilder();
            badges.Append(isGate ? """<span class="hk-badge">gate</span> """ : """<span class="hk-badge" style="background:#f0fdf4;color:#15803d;border-color:#bbf7d0">effect</span> """);
            if (!onEntry)      badges.Append("""<span class="hk-badge" style="background:#fffbeb;color:#92400e;border-color:#fde68a">on-exit</span> """);
            if (totalSent > 0) badges.Append($"""<span class="hk-badge">{totalSent}× sent</span> """);
            if (retries > 0)   badges.Append($"""<span class="hk-badge" style="background:#fdf2f8;color:#9d174d;border-color:#fbcfe8">{retries} retr.</span>""");

            sb.Append($"""
          <tr>
            <td><span class="hk-seq">#{E(orderSeq)}</span><div class="hk-name">{E(display)}</div>{subHtml}{sentHtml}</td>
            <td><span class="hk-pill {pillCls}">{E(pillLbl)}</span></td>
            <td>{badges}</td>
          </tr>
""");
        }
        sb.Append("""
        </tbody>
      </table>
""");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string S(JsonElement el, string key) {
        if (!el.TryGetProperty(key, out var v)) return string.Empty;
        return v.ValueKind switch {
            JsonValueKind.String => v.GetString() ?? string.Empty,
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            _                    => string.Empty
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
