using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Haley.Enums;

namespace Haley.Services;

/// <summary>
/// Converts a timeline JSON string (from IWorkFlowEngine.GetTimelineJsonAsync)
/// into a self-contained, browser-ready HTML page.
/// Design: Light Glass (Design A) — pure CSS, no external frameworks.
/// </summary>
internal static class TimelineHtmlRenderer {

    public static string Render(string timelineJson, string? displayName = null, TimelineDetail detail = TimelineDetail.Detailed) {
        using var doc = JsonDocument.Parse(timelineJson);
        var root = doc.RootElement;

        var sb = new StringBuilder(16_000);
        WriteHead(sb);

        sb.Append("""
<div class="shell">

""");

        if (root.TryGetProperty("instance", out var inst) &&
            root.TryGetProperty("timeline", out var tl) &&
            tl.ValueKind == JsonValueKind.Array) {

            var items    = tl.EnumerateArray().ToList();
            var count    = items.Count;
            var totalDur = count > 0 ? Dur(S(inst, "created"), S(inst, "modified")) : "—";

            WriteHeader(sb, inst, displayName);

            sb.Append($"""
  <div class="controls">
    <button class="btn" id="btn-compact" onclick="toggleCompact()">⊟ Compact</button>
    <span class="steps-label" id="steps-lbl">{count} transition{(count != 1 ? "s" : "")}  ·  {E(totalDur)} total</span>
  </div>

  <div class="scroll-area">
    <div class="tl" id="tl-wrap">

""");

            var prevWasTerminal = false;
            for (var i = 0; i < items.Count; i++) {
                WriteItem(sb, items[i], i, isLast: i == items.Count - 1,
                    next: i + 1 < items.Count ? (JsonElement?)items[i + 1] : null,
                    prevWasTerminal: prevWasTerminal, detail: detail);
                prevWasTerminal = B(items[i], "is_terminal");
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

    private static void WriteHead(StringBuilder sb) {
        sb.Append("""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>Workflow Timeline</title>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    :root {
      --bg:      #f0f4f8;
      --surface: #ffffff;
      --surface2:#f8fafc;
      --border:  #dde3ec;
      --text:    #1a202c;
      --muted:   #64748b;
      --accent:  #2563eb;
      --accent2: #1d4ed8;
      --green:   #16a34a;
      --amber:   #b45309;
      --red:     #dc2626;
      --teal:    #0f766e;
    }
    html, body { height: 100%; background: var(--bg); color: var(--text); font-family: -apple-system, 'Segoe UI', sans-serif; font-size: 14px; }
    body { display: flex; flex-direction: column; overflow: hidden; }

    ::-webkit-scrollbar { width: 5px; }
    ::-webkit-scrollbar-track { background: transparent; }
    ::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 3px; }

    /* Layout */
    .shell { max-width: 780px; width: 100%; margin: 0 auto; padding: 28px 20px 0; display: flex; flex-direction: column; height: 100vh; }
    .scroll-area { flex: 1; overflow-y: auto; padding-bottom: 40px; }

    /* Header card */
    .hdr {
      background: linear-gradient(135deg, #e8f0fe 0%, #ffffff 100%);
      border: 1px solid var(--border); border-radius: 12px;
      padding: 20px 24px 16px; margin-bottom: 16px;
      position: relative; overflow: hidden;
      box-shadow: 0 1px 4px rgba(0,0,0,.06);
    }
    .hdr::before {
      content: ''; position: absolute; inset: 0;
      background: radial-gradient(ellipse at 80% 20%, rgba(37,99,235,.06) 0%, transparent 60%);
      pointer-events: none;
    }
    .hdr-top { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
    .hdr-def { font-size: 10px; text-transform: uppercase; letter-spacing: .1em; color: var(--accent); font-weight: 700; margin-bottom: 6px; }
    .hdr-entity { font-size: 18px; font-weight: 700; color: var(--text); word-break: break-all; }
    .hdr-guid { font-size: 11px; font-family: monospace; color: var(--muted); margin-top: 3px; }
    .state-badge { display: inline-block; padding: 6px 14px; border-radius: 8px; font-weight: 700; font-size: 13px; background: var(--accent2); color: #fff; flex-shrink: 0; }
    .hdr-stats { display: flex; flex-wrap: wrap; gap: 20px; margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border); align-items: center; }
    .hdr-stat { display: flex; flex-direction: column; gap: 2px; }
    .hdr-stat-label { font-size: 10px; text-transform: uppercase; letter-spacing: .08em; color: var(--muted); font-weight: 600; }
    .hdr-stat-val { font-size: 12px; color: var(--text); font-weight: 500; }
    .status-pill { display: inline-block; padding: 2px 8px; border-radius: 20px; font-size: 11px; font-weight: 600; }
    .status-active    { background: #dcfce7; color: #15803d; border: 1px solid #bbf7d0; }
    .status-completed { background: #dbeafe; color: #1d4ed8; border: 1px solid #bfdbfe; }
    .status-failed    { background: #fee2e2; color: #dc2626; border: 1px solid #fecaca; }
    .status-suspended { background: #fef3c7; color: #b45309; border: 1px solid #fde68a; }
    .status-archived  { background: #f1f5f9; color: #64748b; border: 1px solid #e2e8f0; }
    .status-none      { background: #f1f5f9; color: #64748b; border: 1px solid #e2e8f0; }
    .msg-badge { font-size: 11px; padding: 3px 10px; border-radius: 6px; background: #fee2e2; color: #991b1b; border: 1px solid #fca5a5; max-width: 260px; word-break: break-word; }

    /* Controls */
    .controls { display: flex; align-items: center; gap: 8px; margin-bottom: 14px; }
    .btn { display: inline-flex; align-items: center; gap: 5px; padding: 5px 12px; border-radius: 6px; font-size: 12px; font-weight: 600; cursor: pointer; border: 1px solid var(--border); background: var(--surface); color: var(--muted); transition: all .15s; }
    .btn:hover { border-color: var(--accent); color: var(--accent); }
    .btn.active { background: #dbeafe; border-color: var(--accent); color: var(--accent); }
    .steps-label { font-size: 11px; color: var(--muted); margin-left: auto; }

    /* Timeline */
    .tl { position: relative; }
    .tl-item { display: flex; gap: 0; margin-bottom: 4px; }

    /* Rail */
    .tl-rail { width: 48px; flex-shrink: 0; display: flex; flex-direction: column; align-items: center; }
    .tl-dot {
      width: 32px; height: 32px; border-radius: 50%; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
      font-size: 12px; font-weight: 700; z-index: 1; border: 2px solid;
    }
    .dot-normal { background: #eff6ff; border-color: var(--accent);  color: var(--accent); }
    .dot-start  { background: #dcfce7; border-color: var(--green);   color: var(--green); }
    .dot-end    { background: #dbeafe; border-color: var(--accent2); color: var(--accent2); }
    .dot-reopen { background: #fef3c7; border-color: var(--amber);   color: var(--amber); }
    .tl-line { flex: 1; width: 2px; background: linear-gradient(to bottom, #cbd5e1, #e2e8f0); margin-top: 2px; min-height: 24px; }

    /* Card */
    .tl-card {
      flex: 1; min-width: 0; margin-bottom: 20px;
      background: var(--surface); border: 1px solid var(--border);
      border-radius: 10px; overflow: hidden;
      transition: border-color .15s, box-shadow .15s;
      box-shadow: 0 1px 3px rgba(0,0,0,.05);
    }
    .tl-card:hover { border-color: #93c5fd; box-shadow: 0 4px 16px rgba(37,99,235,.1); }

    /* Banner */
    .card-banner {
      display: flex; align-items: center; gap: 12px; padding: 12px 16px;
      background: linear-gradient(90deg, #f8fafc 0%, #f1f5f9 100%);
      border-bottom: 1px solid var(--border);
      cursor: pointer; user-select: none; transition: background .12s;
    }
    .card-banner:hover { background: linear-gradient(90deg, #eff6ff 0%, #e0eaff 100%); }
    .state-from, .state-to { flex: 1; min-width: 0; }
    .state-label { font-size: 10px; text-transform: uppercase; letter-spacing: .08em; color: var(--muted); margin-bottom: 2px; }
    .state-name  { font-size: 13px; font-weight: 700; color: var(--text); }
    .arrow-icon  { color: var(--accent); flex-shrink: 0; font-size: 16px; }
    .banner-tag  { font-size: 10px; font-weight: 700; padding: 2px 8px; border-radius: 4px; }
    .tag-start  { background: #dcfce7; color: var(--green);  border: 1px solid #bbf7d0; }
    .tag-end    { background: #dbeafe; color: var(--accent); border: 1px solid #bfdbfe; }
    .tag-reopen { background: #fef3c7; color: var(--amber);  border: 1px solid #fde68a; }

    /* Meta row */
    .card-meta { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; padding: 10px 16px; border-bottom: 1px solid var(--border); flex-wrap: wrap; }
    .event-name  { font-size: 13px; font-weight: 600; color: var(--text); }
    .code-badge  { font-size: 10px; font-family: monospace; padding: 1px 6px; border-radius: 4px; background: #eff6ff; color: var(--accent); border: 1px solid #bfdbfe; margin-left: 6px; }
    .lc-badge    { font-size: 10px; font-family: monospace; padding: 1px 6px; border-radius: 4px; background: var(--surface2); color: var(--muted); border: 1px solid var(--border); margin-left: 4px; }
    .actor-text  { font-size: 11px; color: var(--muted); font-family: monospace; margin-top: 3px; }
    .meta-right  { text-align: right; flex-shrink: 0; }
    .meta-time   { font-size: 11px; color: var(--muted); }
    .act-count   { font-size: 11px; color: var(--accent); margin-top: 2px; }

    /* Activities */
    .card-acts { padding: 10px 16px; }
    .act-row { display: flex; align-items: center; gap: 10px; padding: 8px 10px; border-radius: 8px; margin-bottom: 6px; background: var(--surface2); border: 1px solid var(--border); transition: background .12s; }
    .act-row:last-child { margin-bottom: 0; }
    .act-row:hover { background: #f1f5f9; }
    .act-activity { font-size: 11px; font-family: monospace; color: var(--text); flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .act-actor    { font-size: 10px; color: var(--muted); margin-top: 2px; font-family: monospace; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .act-pills    { display: flex; flex-direction: column; align-items: flex-end; gap: 4px; flex-shrink: 0; }
    .act-status   { font-size: 10px; font-weight: 600; padding: 2px 7px; border-radius: 12px; }
    .s-approved { background: #dcfce7; color: #15803d; border: 1px solid #bbf7d0; }
    .s-rejected  { background: #fee2e2; color: #dc2626; border: 1px solid #fecaca; }
    .s-pending   { background: #fef3c7; color: #b45309; border: 1px solid #fde68a; }
    .act-dur     { font-size: 10px; color: var(--muted); }
    .no-acts     { font-size: 11px; color: var(--muted); font-style: italic; padding: 6px 2px; }

    /* Hooks (Admin detail) */
    .hooks-hdr { font-size: 10px; text-transform: uppercase; letter-spacing: .08em; color: var(--teal); font-weight: 700; margin: 10px 0 6px; padding-top: 8px; border-top: 1px dashed var(--border); }
    .hook-row { display: flex; align-items: center; gap: 10px; padding: 7px 10px; border-radius: 8px; margin-bottom: 5px; background: #f0fdf9; border: 1px solid #99f6e4; transition: background .12s; }
    .hook-row:last-child { margin-bottom: 0; }
    .hook-row:hover { background: #ccfbf1; }
    .hook-route { font-size: 11px; font-family: monospace; color: var(--teal); flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 600; }
    .hook-meta  { font-size: 10px; color: var(--muted); margin-top: 2px; }
    .hook-pills { display: flex; flex-direction: column; align-items: flex-end; gap: 3px; flex-shrink: 0; }
    .hook-ack   { font-size: 10px; font-weight: 600; padding: 2px 7px; border-radius: 12px; }
    .hk-ok  { background: #dcfce7; color: #15803d; border: 1px solid #bbf7d0; }
    .hk-fail{ background: #fee2e2; color: #dc2626; border: 1px solid #fecaca; }
    .hk-pend{ background: #fef3c7; color: #b45309; border: 1px solid #fde68a; }
    .hk-wait{ background: #f1f5f9; color: #64748b; border: 1px solid #e2e8f0; }
    .hk-badge { font-size: 9px; padding: 1px 5px; border-radius: 4px; background: #e0f2fe; color: #0369a1; border: 1px solid #bae6fd; }

    /* Gap */
    .tl-gap { display: flex; align-items: center; gap: 8px; padding: 0 0 8px 56px; }
    .tl-gap-line { flex: 1; height: 1px; background: var(--border); max-width: 80px; }
    .tl-gap-text { font-size: 10px; color: var(--muted); }

    /* Compact mode */
    .compact-row { display: none; flex-direction: column; gap: 3px; padding: 7px 16px; cursor: pointer; transition: background .12s; }
    .compact-row:hover { background: #f5f8ff; }
    .compact .card-banner,
    .compact .card-meta,
    .compact .card-acts { display: none !important; }
    .compact .tl-gap  { display: none; }
    .compact .tl-card { margin-bottom: 2px; }
    .compact .tl-line { min-height: 2px; }
    .compact .compact-row { display: flex; }
    .compact .force-open .compact-row { display: none !important; }
    .compact .force-open .card-banner { display: flex !important; }
    .compact .force-open .card-meta   { display: flex !important; }
    .compact .force-open .card-acts   { display: block !important; }
    .compact .force-open { margin-bottom: 12px; }
    .cr-line1 { display: flex; align-items: center; gap: 8px; }
    .cr-line2 { display: flex; align-items: center; gap: 8px; min-height: 16px; }
    .cr-step  { font-size: 12px; font-weight: 600; color: var(--text); }
    .cr-arrow { color: var(--accent); font-size: 13px; }
    .cr-spacer { flex: 1; }
    .cr-time  { font-size: 11px; color: var(--muted); font-family: monospace; flex-shrink: 0; }
    .cr-actor { font-size: 10px; color: var(--muted); font-family: monospace; flex-shrink: 0; }
  </style>
  <script>
    function toggleCard(idx) {
      var wrap = document.getElementById('tl-wrap');
      if (!wrap.classList.contains('compact')) return;
      var card = document.getElementById('card-' + idx);
      if (card) card.classList.toggle('force-open');
    }
    function toggleCompact() {
      var wrap = document.getElementById('tl-wrap');
      var btn  = document.getElementById('btn-compact');
      var on   = wrap.classList.toggle('compact');
      wrap.querySelectorAll('.force-open').forEach(function(c) { c.classList.remove('force-open'); });
      btn.classList.toggle('active', on);
      btn.textContent = on ? '\u229e Expanded' : '\u229f Compact';
    }
  </script>
</head>
<body>
""");
    }

    // ── Header ───────────────────────────────────────────────────────────────

    private static void WriteHeader(StringBuilder sb, JsonElement inst, string? displayName) {
        var entityId = S(inst, "entity_id");
        var guid     = S(inst, "guid");
        var label    = !string.IsNullOrWhiteSpace(displayName) ? displayName : entityId;
        var defName  = S(inst, "def_name");
        var defVer   = S(inst, "def_version");
        var curState = S(inst, "current_state");
        var lastEvt  = SplitCamel(S(inst, "last_event"));
        var created  = FmtFull(S(inst, "created"));
        var modified = FmtFull(S(inst, "modified"));
        var totalDur = Dur(S(inst, "created"), S(inst, "modified"));
        var status   = S(inst, "instance_status");
        var message  = S(inst, "instance_message");

        var statusCls = status switch {
            "Active"    => "status-active",
            "Completed" => "status-completed",
            "Failed"    => "status-failed",
            "Suspended" => "status-suspended",
            "Archived"  => "status-archived",
            _           => "status-none"
        };

        var msgHtml = message.Length > 0
            ? $"""<div class="hdr-stat"><span class="hdr-stat-label">Message</span><span class="msg-badge">{E(message)}</span></div>"""
            : string.Empty;

        sb.Append($"""
  <div class="hdr">
    <div class="hdr-top">
      <div style="min-width:0">
        <div class="hdr-def">{E(defName)} <span style="opacity:.5;font-weight:400">·</span> v{E(defVer)}</div>
        <div class="hdr-entity">{E(label)}</div>
        <div class="hdr-guid">{E(entityId)}</div>
        <div class="hdr-guid" style="opacity:.7">{E(guid)}</div>
      </div>
      <div style="text-align:right;flex-shrink:0">
        <div style="font-size:10px;text-transform:uppercase;letter-spacing:.08em;color:var(--muted);margin-bottom:5px">Current State</div>
        <div class="state-badge">{E(curState)}</div>
      </div>
    </div>
    <div class="hdr-stats">
      <div class="hdr-stat">
        <span class="hdr-stat-label">Status</span>
        <span class="hdr-stat-val"><span class="status-pill {statusCls}">{E(status)}</span></span>
      </div>
      <div class="hdr-stat">
        <span class="hdr-stat-label">Last Event</span>
        <span class="hdr-stat-val">{E(lastEvt)}</span>
      </div>
      <div class="hdr-stat">
        <span class="hdr-stat-label">Started</span>
        <span class="hdr-stat-val">{E(created)}</span>
      </div>
      <div class="hdr-stat">
        <span class="hdr-stat-label">Last Updated</span>
        <span class="hdr-stat-val">{E(modified)}</span>
      </div>
      <div class="hdr-stat">
        <span class="hdr-stat-label">Duration</span>
        <span class="hdr-stat-val">{E(totalDur)}</span>
      </div>
      {msgHtml}
    </div>
  </div>

""");
    }

    // ── Timeline item ─────────────────────────────────────────────────────────

    private static void WriteItem(StringBuilder sb, JsonElement item, int idx, bool isLast, JsonElement? next, bool prevWasTerminal, TimelineDetail detail = TimelineDetail.Detailed) {
        var fromState  = S(item, "from_state");
        var toState    = S(item, "to_state");
        var evtName    = SplitCamel(S(item, "event"));
        var evtCode    = S(item, "event_code");
        var lcId       = S(item, "lifecycle_id");
        var actor      = S(item, "actor");
        var created    = Fmt(S(item, "created"));
        var isInitial  = B(item, "is_initial");
        var isTerminal = B(item, "is_terminal");
        var isReopen   = idx > 0 && prevWasTerminal;
        item.TryGetProperty("activities", out var acts);
        item.TryGetProperty("hooks", out var hooks);
        var actCount  = acts.ValueKind  == JsonValueKind.Array ? acts.GetArrayLength()  : 0;
        var hookCount = hooks.ValueKind == JsonValueKind.Array ? hooks.GetArrayLength() : 0;

        var dotCls = isInitial ? "dot-start" : isTerminal ? "dot-end" : isReopen ? "dot-reopen" : "dot-normal";

        var tag = (isInitial, isTerminal, isReopen) switch {
            (true, _, _) => """<span class="banner-tag tag-start">Workflow Started</span>""",
            (_, true, _) => """<span class="banner-tag tag-end">Workflow Ended</span>""",
            (_, _, true) => """<span class="banner-tag tag-reopen">Reopened</span>""",
            _            => string.Empty
        };

        var tagInBanner  = tag.Length > 0 ? $"""<div style="margin-left:8px">{tag}</div>""" : string.Empty;
        var lineHtml     = isLast ? string.Empty : """<div class="tl-line"></div>""";
        var actCountHtml = (actCount > 0 || hookCount > 0)
            ? $"""<div class="act-count">{(actCount > 0 ? $"{actCount} activit{(actCount != 1 ? "ies" : "y")}" : "")}{(actCount > 0 && hookCount > 0 ? " · " : "")}{(hookCount > 0 ? $"{hookCount} hook{(hookCount != 1 ? "s" : "")}" : "")}</div>"""
            : string.Empty;

        sb.Append($"""
  <div class="tl-item">
    <div class="tl-rail">
      <div class="tl-dot {dotCls}">{idx + 1}</div>
      {lineHtml}
    </div>
    <div class="tl-card" id="card-{idx}">
      <div class="compact-row" onclick="toggleCard({idx})">
        <div class="cr-line1">
          <span class="cr-step">{E(fromState)}</span>
          <span class="cr-arrow">→</span>
          <span class="cr-step">{E(toState)}</span>
          <span class="cr-spacer"></span>
          <span class="cr-time">{E(created)}</span>
        </div>
        <div class="cr-line2">
          {tag}
          <span class="cr-spacer"></span>
          <span class="cr-actor">{E(actor)}</span>
        </div>
      </div>
      <div class="card-banner" onclick="toggleCard({idx})">
        <div class="state-from">
          <div class="state-label">From</div>
          <div class="state-name">{E(fromState)}</div>
        </div>
        <div class="arrow-icon">→</div>
        <div class="state-to" style="text-align:right">
          <div class="state-label">To</div>
          <div class="state-name">{E(toState)}</div>
        </div>
        {tagInBanner}
      </div>
      <div class="card-meta">
        <div>
          <div>
            <span class="event-name">{E(evtName)}</span>
            <span class="code-badge">evt:{E(evtCode)}</span>
            <span class="lc-badge">lc:{E(lcId)}</span>
          </div>
          <div class="actor-text">{E(actor)}</div>
        </div>
        <div class="meta-right">
          <div class="meta-time">{E(created)}</div>
          {actCountHtml}
        </div>
      </div>
      <div class="card-acts">

""");

        WriteActivities(sb, acts);
        if (detail >= TimelineDetail.Admin) WriteHooks(sb, hooks);

        sb.Append("""
      </div>
    </div>
  </div>

""");

        if (!isLast && next.HasValue) {
            var gap = Dur(S(item, "created"), S(next.Value, "created"));
            sb.Append($"""
  <div class="tl-gap">
    <div class="tl-gap-line"></div>
    <div class="tl-gap-text">{E(gap)} to next step</div>
  </div>

""");
        }
    }

    // ── Activities ────────────────────────────────────────────────────────────

    private static void WriteActivities(StringBuilder sb, JsonElement acts) {
        if (acts.ValueKind != JsonValueKind.Array || acts.GetArrayLength() == 0) {
            sb.Append("""
        <p class="no-acts">No activities recorded.</p>
""");
            return;
        }

        foreach (var act in acts.EnumerateArray()) {
            var activity    = S(act, "activity");
            var label       = S(act, "label");
            var displayName = !string.IsNullOrWhiteSpace(label) ? label : activity;
            var actorId     = S(act, "actor_id");
            var status      = S(act, "status");
            var dur         = Dur(S(act, "created"), S(act, "modified"));
            var statusCls   = status.ToLowerInvariant() switch {
                "approved" => "s-approved",
                "rejected" => "s-rejected",
                _          => "s-pending"
            };

            sb.Append($"""
        <div class="act-row">
          <div style="flex:1;min-width:0">
            <div class="act-activity">{E(displayName)}</div>
            <div class="act-actor">{E(actorId)}</div>
          </div>
          <div class="act-pills">
            <span class="act-status {statusCls}">{E(status)}</span>
            <span class="act-dur">{E(dur)}</span>
          </div>
        </div>

""");
        }
    }

    // ── Hooks (Admin) ────────────────────────────────────────────────────────

    private static void WriteHooks(StringBuilder sb, JsonElement hooks) {
        if (hooks.ValueKind != JsonValueKind.Array || hooks.GetArrayLength() == 0) return;

        sb.Append("""
        <div class="hooks-hdr">Hooks / Emits</div>
""");

        foreach (var h in hooks.EnumerateArray()) {
            var route      = S(h, "route");
            var label      = S(h, "label");
            var display    = !string.IsNullOrWhiteSpace(label) ? label : route;
            var blocking   = B(h, "blocking");
            var onEntry    = B(h, "on_entry");
            var dispatched = B(h, "dispatched");
            var total      = h.TryGetProperty("total_acks",     out var tv) ? tv.GetInt32() : 0;
            var processed  = h.TryGetProperty("processed_acks", out var pv) ? pv.GetInt32() : 0;
            var failed     = h.TryGetProperty("failed_acks",    out var fv) ? fv.GetInt32() : 0;
            var retries    = h.TryGetProperty("max_retries",    out var rv) ? rv.GetInt32() : 0;

            var ackCls = !dispatched ? "hk-wait"
                       : failed > 0  ? "hk-fail"
                       : total > 0 && processed == total ? "hk-ok"
                       : "hk-pend";
            var ackLbl = !dispatched ? "Not Dispatched"
                       : failed > 0  ? $"Failed ({failed}/{total})"
                       : total > 0   ? $"ACKed {processed}/{total}"
                       : "Dispatched";

            var badges = new StringBuilder();
            if (blocking)        badges.Append("""<span class="hk-badge">blocking</span>""");
            if (!onEntry)        badges.Append("""<span class="hk-badge" style="background:#fef9c3;color:#854d0e;border-color:#fde047">on-exit</span>""");
            if (retries > 0)     badges.Append($"""<span class="hk-badge" style="background:#fce7f3;color:#9d174d;border-color:#f9a8d4">{retries} retr{(retries != 1 ? "ies" : "y")}</span>""");

            var metaText = string.IsNullOrWhiteSpace(label) ? string.Empty : $"""<div class="hook-meta">{E(route)}</div>""";

            sb.Append($"""
        <div class="hook-row">
          <div style="flex:1;min-width:0">
            <div class="hook-route">{E(display)}</div>
            {metaText}
          </div>
          <div class="hook-pills">
            <span class="hook-ack {ackCls}">{E(ackLbl)}</span>
            <div style="display:flex;gap:3px;flex-wrap:wrap;justify-content:flex-end">{badges}</div>
          </div>
        </div>

""");
        }
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
            ? d.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture)
            : dt;

    private static string Fmt(string dt) =>
        TryParse(dt, out var d)
            ? d.ToLocalTime().ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)
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
