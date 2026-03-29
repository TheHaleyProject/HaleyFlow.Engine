using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Haley.Enums;

namespace Haley.Services;

/// <summary>
/// Converts a timeline JSON string into a self-contained HTML page.
/// Design: Control Board (D) — fixed control card, scrollable left rail summary,
/// sticky right-side filter bar, and rich transition cards with activity and hook panels.
/// Primary theme uses green accents.
/// </summary>
internal static class ControlBoardTLR {

    public static string Render(string timelineJson, string? displayName = null, TimelineDetail detail = TimelineDetail.Detailed, string? color = null) {
        using var doc = JsonDocument.Parse(timelineJson);
        var root = doc.RootElement;
        var pageTitle = BuildPageTitle(root);

        var sb = new StringBuilder(24_000);
        WriteHead(sb, pageTitle, color);
        sb.Append("<div class=\"shell\">\n\n");

        if (root.TryGetProperty("instance", out var inst) &&
            root.TryGetProperty("timeline", out var tl) &&
            tl.ValueKind == JsonValueKind.Array) {

            var items = tl.EnumerateArray().ToList();
            var count = items.Count;
            var totalDur = count > 0 ? Dur(S(inst, "created"), S(inst, "modified")) : "—";
            var loops = CountLoops(items);
            var activityCount = CountActivities(items);
            var hookCount = detail >= TimelineDetail.Admin ? CountHooks(items) : 0;
            var boardTitle = !string.IsNullOrWhiteSpace(displayName) ? displayName : S(inst, "entity_id");

            sb.Append("""
  <aside class="side">
""");
            WriteInstanceCard(sb, inst, displayName, count, totalDur);
            sb.Append("""
    <div class="side-scroll">
""");
            WriteSummary(sb, inst, count, activityCount, hookCount, loops, totalDur);
            WriteStatePath(sb, items, S(inst, "current_state"));
            sb.Append("""
    </div>
  </aside>

  <section class="board">
""");
            WriteBoardHeader(sb, boardTitle);
            WriteFilterPanel(sb, count);
            sb.Append("""
    <div class="entries" id="entries-area">
""");

            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < items.Count; i++) {
                var item = items[i];
                var toState = S(item, "to_state");
                var isLoop = !string.IsNullOrWhiteSpace(toState) && seenTargets.Contains(toState);
                WriteEntry(sb, item, i, isLoop, detail);
                if (!string.IsNullOrWhiteSpace(toState)) seenTargets.Add(toState);
            }

            sb.Append("""
    </div>
  </section>

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
      --bg:           #f4f5f7;
      --bg2:          #eceff2;
      --panel:        #ffffff;
      --panel-soft:   #fafbfc;
      --line:         #d9dee4;
      --text:         #203026;
      --muted:        #65796b;
      --brand:        #24b54d;
      --brand-deep:   #188439;
      --brand-soft:   #d8f5df;
      --brand-soft-2: #eefbf1;
      --blue-soft:    #dfeaff;
      --blue-text:    #2454b5;
      --amber-soft:   #fff2cc;
      --amber-text:   #946200;
      --red-soft:     #f7dada;
      --red-text:     #b42318;
      --shadow:       0 16px 38px rgba(15, 23, 42, .08);
    }
    html, body { margin: 0; }
    body {
      background: linear-gradient(180deg, var(--bg2), var(--bg));
      color: var(--text);
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
      font-size: 14px;
    }
    ::-webkit-scrollbar { width: 6px; height: 6px; }
    ::-webkit-scrollbar-thumb { background: #c2d8c7; border-radius: 999px; }
    .shell { max-width: 1680px; margin: 16px auto; padding: 0 16px 32px; display: grid; grid-template-columns: 384px minmax(0,1fr); gap: 18px; align-items: start; }
    .panel { background: var(--panel); border: 1px solid var(--line); border-radius: 24px; box-shadow: var(--shadow); }
    .pad { padding: 18px; }
    .instance-card { display: flex; flex-direction: column; gap: 8px; }
    .inst-entity { font-size: 20px; font-weight: 900; line-height: 1.15; overflow-wrap: anywhere; }
    .inst-id-row { display: flex; flex-direction: column; gap: 1px; }
    .inst-id-label { font-size: 10px; text-transform: uppercase; letter-spacing: .12em; font-weight: 800; color: var(--muted); }
    .inst-guid { font-size: 11px; font-family: Consolas, monospace; color: var(--text); overflow-wrap: anywhere; }
    .inst-status-block { margin-top: 6px; text-align: center; padding: 14px 10px; border-radius: 16px; font-size: 22px; font-weight: 900; letter-spacing: .06em; text-transform: uppercase; }
    .inst-status-block.s-active    { background: var(--brand-soft);  color: var(--brand-deep); }
    .inst-status-block.s-completed { background: var(--blue-soft);   color: var(--blue-text); }
    .inst-status-block.s-failed    { background: var(--red-soft);    color: var(--red-text); }
    .inst-status-block.s-suspended { background: var(--amber-soft);  color: var(--amber-text); }
    .inst-status-block.s-none      { background: #edf4ef;            color: var(--muted); }
    .inst-chips { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 8px; justify-content: center; }
    .inst-chip { padding: 5px 10px; border-radius: 999px; font-size: 11px; font-weight: 800; color: var(--brand-deep); background: var(--brand-soft-2); border: 1px solid var(--line); }
    .eyebrow { font-size: 11px; text-transform: uppercase; letter-spacing: .16em; font-weight: 800; color: var(--brand-deep); margin-bottom: 8px; }
    .entity { font-size: 28px; line-height: 1.02; font-weight: 900; }
    .guid { margin-top: 7px; color: var(--muted); font-size: 12px; font-family: Consolas, monospace; }
    .head-meta { margin-top: 10px; display: flex; flex-wrap: wrap; gap: 8px; }
    .head-pill { display: inline-flex; align-items: center; padding: 6px 10px; border-radius: 999px; font-size: 11px; font-weight: 800; color: var(--brand-deep); background: var(--brand-soft-2); border: 1px solid var(--line); }
    .stats { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
    .stat { border: 1px solid var(--line); border-radius: 18px; padding: 12px 14px; background: rgba(255,255,255,.72); min-width: 0; }
    .stat-k { font-size: 10px; text-transform: uppercase; letter-spacing: .12em; color: var(--muted); font-weight: 800; }
    .stat-v { margin-top: 6px; font-size: 18px; font-weight: 900; line-height: 1.15; overflow-wrap: anywhere; }
    .status-pill { display: inline-flex; align-items: center; gap: 6px; padding: 7px 12px; border-radius: 999px; font-size: 11px; text-transform: uppercase; letter-spacing: .08em; font-weight: 900; }
    .s-active { background: var(--brand-soft); color: var(--brand-deep); }
    .s-completed {
      background: var(--blue-soft); color: var(--blue-text);
      padding: 10px 16px; font-size: 13px; letter-spacing: .12em;
      box-shadow: inset 0 0 0 1px rgba(36,84,181,.16);
    }
    .s-failed { background: var(--red-soft); color: var(--red-text); }
    .s-suspended { background: var(--amber-soft); color: var(--amber-text); }
    .s-none { background: #edf4ef; color: var(--muted); }
    .side {
      position: sticky; top: 16px; align-self: start; max-height: calc(100vh - 32px);
      display: flex; flex-direction: column; gap: 16px;
    }
    .instance-card { flex: 0 0 auto; }
    .side-scroll { min-height: 0; overflow-y: auto; display: flex; flex-direction: column; gap: 16px; padding-right: 4px; }
    .summary-panel, .state-panel { display: flex; flex-direction: column; flex: 0 0 auto; }
    .panel-title { font-size: 12px; text-transform: uppercase; letter-spacing: .14em; color: var(--brand-deep); font-weight: 800; margin-bottom: 12px; }
    .summary-list { display: grid; gap: 10px; }
    .summary-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding-bottom: 10px; border-bottom: 1px dashed var(--line); font-size: 13px; }
    .summary-row:last-child { border-bottom: none; padding-bottom: 0; }
    .summary-k { color: var(--muted); font-weight: 700; }
    .summary-v { font-weight: 900; }
    .state-list { display: flex; flex-direction: column; gap: 10px; }
    .state-card { border: 1px solid var(--line); border-radius: 18px; padding: 12px 14px; background: linear-gradient(180deg, #ffffff, var(--panel-soft)); display: flex; align-items: center; justify-content: space-between; gap: 10px; }
    .state-card.current { border-color: var(--brand); box-shadow: inset 0 0 0 1px rgba(36,181,77,.18); }
    .state-name { font-size: 13px; font-weight: 900; }
    .state-meta { margin-top: 4px; font-size: 11px; color: var(--muted); }
    .state-count { width: 30px; height: 30px; border-radius: 999px; display: grid; place-items: center; background: var(--brand-soft); color: var(--brand-deep); font-size: 11px; font-weight: 900; flex-shrink: 0; }
    .board { min-width: 0; display: flex; flex-direction: column; gap: 16px; }
    .board-head { padding: 22px 24px; }
    .board-title { font-size: clamp(1.45rem, 2vw, 2.1rem); line-height: 1.08; font-weight: 900; overflow-wrap: anywhere; }
    .board-toolbar-stick { position: sticky; top: 16px; z-index: 8; }
    .board-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .toolbar-status { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; font-size: 11px; color: var(--muted); font-weight: 800; text-transform: uppercase; letter-spacing: .12em; }
    .toolbar-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .btn { display: inline-flex; align-items: center; justify-content: center; border: 1px solid var(--line); background: #ffffff; color: var(--muted); border-radius: 999px; padding: 8px 14px; font-size: 12px; font-weight: 800; cursor: pointer; transition: all .15s ease; }
    .btn:hover { border-color: var(--brand); color: var(--brand-deep); }
    .btn.active { background: var(--brand); color: #fff; border-color: var(--brand); }
    .entries { display: flex; flex-direction: column; gap: 18px; padding-bottom: 24px; }
    .entry { flex: 0 0 auto; border: 1px solid var(--line); border-radius: 22px; overflow: hidden; background: linear-gradient(180deg, #ffffff, var(--panel-soft)); box-shadow: var(--shadow); }
    .entry-wrap { display: grid; grid-template-columns: 120px minmax(0, 1fr); }
    .entry-index { background: linear-gradient(180deg, var(--brand-deep), #146530); color: #f5fff7; padding: 16px 10px 18px; text-align: center; display: flex; flex-direction: column; align-items: center; justify-content: flex-start; gap: 8px; }
    .entry-no { font-size: 22px; font-weight: 900; line-height: 1; }
    .entry-time { display: grid; gap: 3px; color: rgba(245,255,247,.86); font-family: Consolas, monospace; }
    .entry-date { font-size: 12px; line-height: 1.15; font-weight: 700; }
    .entry-clock { font-size: 18px; line-height: 1.05; font-weight: 900; }
    .entry-main { min-width: 0; padding: 22px 24px 24px; display: flex; flex-direction: column; gap: 14px; }
    .entry-top { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .event-name { font-size: 22px; font-weight: 900; line-height: 1.08; }
    .event-sub { margin-top: 6px; color: var(--muted); font-size: 13px; font-family: Consolas, monospace; }
    .compact-sub { display: none; margin-top: 6px; color: var(--muted); font-size: 12px; font-family: Consolas, monospace; overflow-wrap: anywhere; }
    .tag-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .entry-tag { padding: 4px 8px; border-radius: 999px; font-size: 10px; font-weight: 900; text-transform: uppercase; letter-spacing: .08em; }
    .tag-initial { background: var(--brand-soft); color: var(--brand-deep); }
    .tag-terminal { background: var(--blue-soft); color: var(--blue-text); }
    .tag-loop { background: var(--amber-soft); color: var(--amber-text); }
    .tag-hook { background: #e7fff0; color: var(--brand-deep); }
    .flow { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .flow-from, .flow-to { padding: 10px 14px; border-radius: 14px; font-size: 14px; font-weight: 900; }
    .flow-from { background: #edf5ef; color: #4b5f52; }
    .flow-to { background: var(--brand-soft); color: var(--brand-deep); }
    .flow-arrow { color: var(--muted); font-size: 18px; }
    .meta-line { color: var(--muted); font-size: 13px; display: flex; align-items: center; gap: 18px; flex-wrap: wrap; }
    .meta-line strong { color: var(--text); }
    .detail-stack { display: grid; gap: 14px; align-items: start; }
    .box { min-width: 0; border: 1px solid var(--line); border-radius: 16px; padding: 16px; background: rgba(255,255,255,.74); }
    .box-h { font-size: 11px; text-transform: uppercase; letter-spacing: .10em; color: var(--muted); font-weight: 900; margin-bottom: 10px; }
    .card-list { display: grid; gap: 10px; align-content: start; }
    .activity-card, .hook-card { border-radius: 14px; padding: 11px 12px; }
    .activity-card { border: 1px solid var(--line); background: var(--panel-soft); display: grid; grid-template-columns: 1fr auto; gap: 8px; align-items: start; }
    .activity-name { font-size: 12px; font-weight: 700; font-family: Consolas, monospace; overflow-wrap: anywhere; }
    .activity-label { margin-top: 4px; font-size: 12px; color: var(--muted); overflow-wrap: anywhere; }
    .activity-meta { margin-top: 6px; font-size: 11px; color: var(--muted); font-family: Consolas, monospace; overflow-wrap: anywhere; }
    .stamp { padding: 7px 10px; border-radius: 999px; min-width: 88px; text-align: center; font-size: 10px; font-weight: 900; text-transform: uppercase; letter-spacing: .08em; }
    .approved {
      background: var(--brand-soft); color: var(--brand-deep);
      min-width: 108px; padding: 9px 14px; font-size: 11px; letter-spacing: .11em;
      box-shadow: inset 0 0 0 1px rgba(24,132,57,.14);
    }
    .rejected { background: var(--red-soft); color: var(--red-text); }
    .pending { background: var(--amber-soft); color: var(--amber-text); }
    .other { background: #edf4ef; color: var(--muted); }
    .hook-card { border: 1px solid var(--brand-soft); background: var(--panel-soft); display: grid; grid-template-columns: 28px minmax(0, 1fr) auto; gap: 6px 12px; align-items: center; padding: 10px 12px; }
    .hook-seq-col { font-size: 13px; font-weight: 900; color: var(--brand-deep); font-family: Consolas, monospace; text-align: center; }
    .hook-main { min-width: 0; }
    .hook-route { font-size: 12px; font-weight: 800; color: var(--brand-deep); font-family: Consolas, monospace; line-height: 1.2; overflow-wrap: anywhere; }
    .hook-label { margin-top: 2px; font-size: 11px; color: var(--muted); line-height: 1.2; overflow-wrap: anywhere; }
    .hook-side { min-width: max-content; display: grid; gap: 4px; justify-items: end; text-align: right; }
    .hook-time { font-size: 10px; color: var(--muted); font-family: Consolas, monospace; }
    .hook-meta { display: flex; gap: 6px; flex-wrap: nowrap; justify-content: flex-end; white-space: nowrap; }
    .mini-badge { padding: 3px 7px; border-radius: 999px; font-size: 9px; font-weight: 900; background: #ebf8ee; color: #466553; border: 1px solid #cfead5; }
    .mini-badge.ok   { background: var(--brand-soft);  color: var(--brand-deep); border-color: rgba(36,181,77,.3); }
    .mini-badge.warn { background: var(--amber-soft);  color: var(--amber-text); border-color: rgba(150,100,0,.22); }
    .mini-badge.fail { background: var(--red-soft);    color: var(--red-text);   border-color: rgba(180,35,24,.25); }
    .inst-msg { margin-top: 16px; padding: 12px 14px; border-radius: 14px; border: 1px solid rgba(180,35,24,.22); background: rgba(247,218,218,.55); color: var(--red-text); font-size: 12px; font-family: Consolas,monospace; white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.5; }
    .inst-msg-k { font-size: 10px; text-transform: uppercase; letter-spacing: .12em; font-weight: 800; color: var(--red-text); margin-bottom: 5px; }
    .empty { color: var(--muted); font-size: 12px; font-style: italic; padding: 4px 0; }
    .entries.compact .entry { cursor: pointer; }
    .entries.compact { gap: 12px; }
    .entries.compact .entry-wrap { grid-template-columns: 88px minmax(0, 1fr); }
    .entries.compact .entry-index { padding: 12px 8px 14px; gap: 5px; }
    .entries.compact .entry-no { font-size: 18px; }
    .entries.compact .entry-date { font-size: 11px; }
    .entries.compact .entry-clock { font-size: 14px; }
    .entries.compact .entry-main { padding: 16px 18px; gap: 10px; }
    .entries.compact .event-name { font-size: 16px; }
    .entries.compact .event-sub.full-sub { display: none; }
    .entries.compact .compact-sub { display: block; }
    .entries.compact .entry-tag { font-size: 9px; }
    .entries.compact .hook-card { grid-template-columns: 28px 1fr; }
    .entries.compact .hook-side { justify-items: start; text-align: left; }
    .entries.compact .hook-meta { justify-content: flex-start; flex-wrap: wrap; white-space: normal; }
    .entries.compact .entry .flow,
    .entries.compact .entry .meta-line,
    .entries.compact .entry .detail-stack { display: none; }
    .entries.compact .entry.force-open .flow { display: flex; }
    .entries.compact .entry.force-open .meta-line { display: flex; }
    .entries.compact .entry.force-open .detail-stack { display: grid; }
    .entries.compact .entry.force-open .hook-card { grid-template-columns: 28px minmax(0, 1fr) auto; }
    .entries.compact .entry.force-open .hook-side { justify-items: end; text-align: right; }
    .entries.compact .entry.force-open .hook-meta { justify-content: flex-end; flex-wrap: nowrap; white-space: nowrap; }
    @media (max-width: 1120px) {
      .shell { grid-template-columns: 1fr; }
      .side { position: static; max-height: none; }
      .side-scroll { max-height: none; overflow: visible; padding-right: 0; }
      .board-toolbar-stick { position: static; }
    }
    @media (max-width: 760px) {
      .shell { padding: 14px; }
      .stats { grid-template-columns: 1fr; }
      .entry-wrap { grid-template-columns: 1fr; }
      .entries.compact .entry-wrap { grid-template-columns: 1fr; }
      .entry-index { flex-direction: row; align-items: center; justify-content: space-between; text-align: left; }
      .hook-card { grid-template-columns: 1fr; }
      .hook-side { justify-items: start; text-align: left; }
      .hook-meta { justify-content: flex-start; flex-wrap: wrap; white-space: normal; }
      .entity { font-size: 24px; }
    }
  </style>
  <script>
    function setControlBoardFilter(mode, btn) {
      document.querySelectorAll('.board-toolbar .filter-btn').forEach(function(b) { b.classList.remove('active'); });
      if (btn) btn.classList.add('active');

      var visible = 0;
      document.querySelectorAll('.entry').forEach(function(entry) {
        var show = mode === 'all'
          || (mode === 'hooks' && entry.dataset.hooks === '1')
          || (mode === 'loop' && entry.dataset.loop === '1')
          || (mode === 'edge' && entry.dataset.edge === '1');
        entry.style.display = show ? '' : 'none';
        if (show) visible++;
      });

      var label = document.getElementById('board-visible');
      if (label) label.textContent = visible + ' visible';
    }

    function toggleControlBoardCompact() {
      var area = document.getElementById('entries-area');
      var btn = document.getElementById('board-compact-btn');
      if (!area || !btn) return;

      var on = area.classList.toggle('compact');
      area.querySelectorAll('.entry.force-open').forEach(function(entry) { entry.classList.remove('force-open'); });
      btn.classList.toggle('active', on);
      btn.textContent = on ? 'Full view' : 'Compact';
    }

    function toggleControlBoardEntry(idx) {
      var area = document.getElementById('entries-area');
      if (!area || !area.classList.contains('compact')) return;

      var entry = document.getElementById('entry-' + idx);
      if (entry) entry.classList.toggle('force-open');
    }

    document.addEventListener('DOMContentLoaded', function() {
      var active = document.querySelector('.board-toolbar .filter-btn.active');
      setControlBoardFilter('all', active);
    });
  </script>
""");
        if (RendererColors.TryParse(color, out var c))
            sb.Append($@"  <style>
    :root {{ --brand: {c.Base}; --brand-deep: {c.Dark}; --brand-soft: {c.Border}; --brand-soft-2: {c.Light}; }}
    .entry-index {{ background: linear-gradient(180deg, {c.Dark}, {c.Dark}dd) !important; }}
    .hook-card {{ border-color: {c.Border} !important; }}
    .mini-badge {{ background: {c.Light}; color: {c.Dark}; border-color: {c.Border}; }}
    .tag-initial {{ background: {c.Border}; color: {c.Dark}; }}
    .tag-hook {{ background: {c.Light}; color: {c.Dark}; }}
    .state-count {{ background: {c.Border}; color: {c.Dark}; }}
  </style>
");
        sb.Append("""
</head>
<body>
""");
    }

    // ── Sidebar instance card ────────────────────────────────────────────────

    private static void WriteInstanceCard(StringBuilder sb, JsonElement inst, string? displayName, int count, string totalDur) {
        var entityId  = S(inst, "entity_id");
        var guid      = S(inst, "guid");
        var label     = !string.IsNullOrWhiteSpace(displayName) ? displayName : entityId;
        var defName   = S(inst, "def_name");
        var defVer    = S(inst, "def_version");
        var curState  = S(inst, "current_state");
        var status    = S(inst, "instance_status");
        var statusCls = StatusClass(status);
        var message   = S(inst, "instance_message");

        sb.Append($"""
      <section class="panel pad instance-card">
        <div class="eyebrow">Control board</div>
        <div class="inst-entity">{E(label)}</div>
        <div class="inst-id-row"><span class="inst-id-label">Entity</span><span class="inst-guid">{E(entityId)}</span></div>
        <div class="inst-id-row"><span class="inst-id-label">Instance</span><span class="inst-guid">{E(guid)}</span></div>
        <div class="inst-id-row"><span class="inst-id-label">Definition</span><span class="inst-guid">{E(defName)} v{E(defVer)}</span></div>
        <div class="inst-status-block {statusCls}">{E(status)}</div>
        <div class="inst-chips">
          <span class="inst-chip">{E(curState)}</span>
          <span class="inst-chip">{count} transition{(count == 1 ? string.Empty : "s")}</span>
          <span class="inst-chip">{E(totalDur)} total</span>
        </div>
        {(!string.IsNullOrWhiteSpace(message) ? $"<div class=\"inst-msg\"><div class=\"inst-msg-k\">Message</div>{E(message)}</div>" : string.Empty)}
      </section>
""");
    }

    private static void WriteBoardHeader(StringBuilder sb, string title) {
        sb.Append($"""
    <section class="panel board-head">
      <div class="board-title">{E(title)}</div>
    </section>
""");
    }

    private static void WriteFilterPanel(StringBuilder sb, int visibleCount) {
        sb.Append($"""
    <div class="board-toolbar-stick">
      <section class="panel pad board-toolbar">
        <div class="toolbar-status">
          <span>Current view</span>
          <span id="board-visible">{visibleCount} visible</span>
        </div>
        <div class="toolbar-actions">
          <button class="btn filter-btn active" onclick="setControlBoardFilter('all', this)">All entries</button>
          <button class="btn filter-btn" onclick="setControlBoardFilter('hooks', this)">Has hooks</button>
          <button class="btn filter-btn" onclick="setControlBoardFilter('loop', this)">Loop / re-entry</button>
          <button class="btn filter-btn" onclick="setControlBoardFilter('edge', this)">Initial / terminal</button>
          <button class="btn" id="board-compact-btn" onclick="toggleControlBoardCompact()">Compact</button>
        </div>
      </section>
    </div>
""");
    }
    private static void WriteSummary(StringBuilder sb, JsonElement inst, int transitions, int activities, int hooks, int loops, string totalDur) {
        sb.Append($"""
      <section class="panel pad summary-panel">
        <div class="panel-title">Operational summary</div>
        <div class="summary-list">
          <div class="summary-row"><span class="summary-k">Transitions captured</span><span class="summary-v">{transitions}</span></div>
          <div class="summary-row"><span class="summary-k">Activities logged</span><span class="summary-v">{activities}</span></div>
          <div class="summary-row"><span class="summary-k">Hooks emitted</span><span class="summary-v">{hooks}</span></div>
          <div class="summary-row"><span class="summary-k">Loop / re-entry points</span><span class="summary-v">{loops}</span></div>
          <div class="summary-row"><span class="summary-k">Started</span><span class="summary-v">{E(FmtFull(S(inst, "created")))}</span></div>
          <div class="summary-row"><span class="summary-k">Total duration</span><span class="summary-v">{E(totalDur)}</span></div>
        </div>
      </section>
""");
    }

    private static void WriteStatePath(StringBuilder sb, List<JsonElement> items, string currentState) {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < items.Count; i++) {
            var name = S(items[i], "to_state");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!counts.ContainsKey(name)) {
                counts[name] = 0;
                order.Add(name);
            }
            counts[name] += 1;
        }

        sb.Append("""
      <section class="panel pad state-panel">
        <div class="panel-title">State path</div>
        <div class="state-list">
""");

        for (var i = 0; i < order.Count; i++) {
            var name = order[i];
            var cls = string.Equals(name, currentState, StringComparison.OrdinalIgnoreCase) ? " current" : string.Empty;
            sb.Append($"""
          <div class="state-card{cls}">
            <div>
              <div class="state-name">{E(name)}</div>
              <div class="state-meta">Reached {counts[name]} time{(counts[name] == 1 ? string.Empty : "s")}</div>
            </div>
            <div class="state-count">{counts[name]}</div>
          </div>
""");
        }

        sb.Append("""
        </div>
      </section>
""");
    }

    // ── Entries ──────────────────────────────────────────────────────────────

    private static void WriteEntry(StringBuilder sb, JsonElement item, int idx, bool isLoop, TimelineDetail detail) {
        var fromState  = S(item, "from_state");
        var toState    = S(item, "to_state");
        var eventName  = SplitCamel(S(item, "event"));
        var eventCode  = S(item, "event_code");
        var lcId       = S(item, "lifecycle_id");
        var actor      = S(item, "actor");
        var actorDisplay = string.IsNullOrWhiteSpace(actor) ? "-" : actor;
        var rawCreated = S(item, "created");
        var createdDate = FmtDate(rawCreated);
        var createdTime = FmtTime(rawCreated);
        var isInitial  = B(item, "is_initial");
        var isTerminal = B(item, "is_terminal");

        item.TryGetProperty("activities", out var acts);
        item.TryGetProperty("hooks", out var hooks);
        var actCount  = acts.ValueKind == JsonValueKind.Array ? acts.GetArrayLength() : 0;
        var hookCount = hooks.ValueKind == JsonValueKind.Array ? hooks.GetArrayLength() : 0;
        var hasActs = actCount > 0;
        var hasHooks = hookCount > 0;
        var edge      = isInitial || isTerminal;

        var tags = new StringBuilder();
        if (isInitial) tags.Append("<span class=\"entry-tag tag-initial\">Initial</span>");
        if (isTerminal) tags.Append("<span class=\"entry-tag tag-terminal\">Terminal</span>");
        if (isLoop) tags.Append("<span class=\"entry-tag tag-loop\">Loop</span>");
        if (hookCount > 0) tags.Append($"<span class=\"entry-tag tag-hook\">{hookCount} hook{(hookCount == 1 ? string.Empty : "s")}</span>");

        sb.Append($"""
        <article class="entry" id="entry-{idx}" data-hooks="{(hookCount > 0 ? 1 : 0)}" data-loop="{(isLoop ? 1 : 0)}" data-edge="{(edge ? 1 : 0)}">
          <div class="entry-wrap" onclick="toggleControlBoardEntry({idx})">
            <div class="entry-index">
              <div class="entry-no">{idx + 1}</div>
              <div class="entry-time">
                <div class="entry-date">{E(createdDate)}</div>
                <div class="entry-clock">{E(createdTime)}</div>
              </div>
            </div>
            <div class="entry-main">
              <div class="entry-top">
                <div>
                  <div class="event-name">{E(eventName)}</div>
                  <div class="event-sub full-sub">Lifecycle #{E(lcId)} | event {E(eventCode)}</div>
                  <div class="compact-sub">{E(fromState)} -> {E(toState)} | event {E(eventCode)} | {E(actorDisplay)}</div>
                </div>
                <div class="tag-row">{tags}</div>
              </div>

              <div class="flow">
                <span class="flow-from">{E(fromState)}</span>
                <span class="flow-arrow">→</span>
                <span class="flow-to">{E(toState)}</span>
              </div>

              <div class="meta-line">
                <span>actor <strong>{E(actor)}</strong></span>
                <span>activities <strong>{actCount}</strong></span>
                <span>hooks <strong>{hookCount}</strong></span>
              </div>
""");

        if (hasActs || hasHooks) {
            sb.Append("""
              <div class="detail-stack">
""");

            if (hasActs) {
                sb.Append("""
                <section class="box">
                  <div class="box-h">Business actions</div>
                  <div class="card-list">
""");
                WriteActivityCards(sb, acts);
                sb.Append("""
                  </div>
                </section>
""");
            }

            if (hasHooks) {
                sb.Append("""
                <section class="box">
                  <div class="box-h">Hook dispatch</div>
                  <div class="card-list">
""");

                if (detail >= TimelineDetail.Admin) {
                    WriteHookCards(sb, hooks);
                } else {
                    WriteHookDetailNote(sb, hookCount);
                }

                sb.Append("""
                  </div>
                </section>
""");
            }

            sb.Append("""
              </div>
""");
        }

        sb.Append("""
            </div>
          </div>
        </article>
""");
    }

    private static void WriteActivityCards(StringBuilder sb, JsonElement acts) {
        if (acts.ValueKind != JsonValueKind.Array || acts.GetArrayLength() == 0) {
            sb.Append("                    <div class=\"empty\">No activity rows were recorded for this transition.</div>\n");
            return;
        }

        foreach (var a in acts.EnumerateArray()) {
            var activity = S(a, "activity");
            var label    = S(a, "label");
            var display  = !string.IsNullOrWhiteSpace(label) ? label : activity;
            var actorId  = S(a, "actor_id");
            var status   = S(a, "status");
            var modified = Fmt(S(a, "modified"));
            var stampCls = StatusStampClass(status);

            sb.Append($"""
                    <div class="activity-card">
                      <div>
                        <div class="activity-name">{E(activity)}</div>
                        <div class="activity-label">{E(display)}</div>
                        <div class="activity-meta">{E(actorId)} | {E(modified)}</div>
                      </div>
                      <div class="stamp {stampCls}">{E(status)}</div>
                    </div>
""");
        }
    }

    private static void WriteHookCards(StringBuilder sb, JsonElement hooks) {
        if (hooks.ValueKind != JsonValueKind.Array || hooks.GetArrayLength() == 0) {
            sb.Append("                    <div class=\"empty\">No hook emissions were attached to this lifecycle step.</div>\n");
            return;
        }

        foreach (var h in OrderHooks(hooks)) {
            var route      = S(h, "route");
            var label      = S(h, "label");
            var display    = !string.IsNullOrWhiteSpace(label) ? label : route;
            var secondary  = !string.IsNullOrWhiteSpace(label) ? route : string.Empty;
            var isGate     = h.TryGetProperty("hook_type", out var htv) && htv.TryGetInt32(out var htInt) ? htInt == 1 : true;
            var orderSeq   = S(h, "order_seq");
            var rawTrigger = S(h, "last_trigger");
            var lastSent   = Fmt(rawTrigger);
            var total      = h.TryGetProperty("total_acks", out var tv) ? tv.GetInt32() : 0;
            var processed  = h.TryGetProperty("processed_acks", out var pv) ? pv.GetInt32() : 0;
            var totalSent  = h.TryGetProperty("total_triggers", out var sv) ? sv.GetInt32() : 0;
            var orderLabel = int.TryParse(orderSeq, out var oNum) && oNum > 0 ? $"#{oNum}" : "#\u2013";
            var sentLabel  = !string.IsNullOrWhiteSpace(rawTrigger) ? lastSent : "pending";
            var timeLine   = $"Last sent: {sentLabel}";
            var secondaryHtml = !string.IsNullOrWhiteSpace(secondary)
                ? $"<div class=\"hook-label\">{E(secondary)}</div>"
                : string.Empty;

            // total_triggers = SUM(trigger_count): incremented per monitor retry, NOT on initial dispatch.
            // Actual total dispatches = total_acks (initial, 1 per consumer) + total_triggers (retries).
            var ackedCls        = total > 0 && processed >= total ? "ok"
                                : processed > 0                   ? "warn"
                                : total > 0                       ? "fail"
                                : string.Empty;
            var totalDispatches = total + totalSent;
            var hasRetries      = totalSent > 0;
            var sentCls         = totalDispatches > 0 ? (hasRetries ? "warn" : "ok") : string.Empty;
            var retryHtml       = hasRetries
                ? $"<span class=\"mini-badge fail\">{totalSent} {(totalSent == 1 ? "retry" : "retries")}</span>"
                : string.Empty;

            sb.Append($"""
                    <div class="hook-card">
                      <div class="hook-seq-col">{E(orderLabel)}</div>
                      <div class="hook-main">
                        <div class="hook-route">{E(display)}</div>
                        {secondaryHtml}
                      </div>
                      <div class="hook-side">
                        <div class="hook-time">{E(timeLine)}</div>
                        <div class="hook-meta">
                          <span class="mini-badge {(isGate ? "warn" : string.Empty)}">{(isGate ? "gate" : "effect")}</span>
                          {retryHtml}
                          <span class="mini-badge {sentCls}">{totalDispatches} sent</span>
                          <span class="mini-badge {ackedCls}">acked {processed}/{total}</span>
                        </div>
                      </div>
                    </div>
""");
        }
    }

    private static void WriteHookDetailNote(StringBuilder sb, int hookCount) {
        var msg = hookCount > 0
            ? $"Hook details are available in Admin view. This transition has {hookCount} hook{(hookCount == 1 ? string.Empty : "s")}."
            : "Hook details are available in Admin view when emits exist for this transition.";
        sb.Append($"                    <div class=\"empty\">{E(msg)}</div>\n");
    }
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int CountLoops(List<JsonElement> items) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loops = 0;
        for (var i = 0; i < items.Count; i++) {
            var state = S(items[i], "to_state");
            if (string.IsNullOrWhiteSpace(state)) continue;
            if (seen.Contains(state)) loops++;
            seen.Add(state);
        }
        return loops;
    }

    private static int CountActivities(List<JsonElement> items) {
        var total = 0;
        for (var i = 0; i < items.Count; i++) {
            if (items[i].TryGetProperty("activities", out var acts) && acts.ValueKind == JsonValueKind.Array)
                total += acts.GetArrayLength();
        }
        return total;
    }

    private static int CountHooks(List<JsonElement> items) {
        var total = 0;
        for (var i = 0; i < items.Count; i++) {
            if (items[i].TryGetProperty("hooks", out var hooks) && hooks.ValueKind == JsonValueKind.Array)
                total += hooks.GetArrayLength();
        }
        return total;
    }

    // Order the hook list so sequential emits are rendered in the same order users expect to read them.
    private static IEnumerable<JsonElement> OrderHooks(JsonElement hooks) {
        if (hooks.ValueKind != JsonValueKind.Array) yield break;

        var items = hooks.EnumerateArray()
            .OrderBy(ParseHookOrder)
            .ThenByDescending(h => B(h, "on_entry"))
            .ThenBy(h => S(h, "route"), StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < items.Count; i++)
            yield return items[i];
    }

    private static int ParseHookOrder(JsonElement hook) {
        if (!hook.TryGetProperty("order_seq", out var value)) return int.MaxValue;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedNumber))
            return parsedNumber > 0 ? parsedNumber : int.MaxValue;

        return int.TryParse(value.ToString(), out var parsedText) && parsedText > 0
            ? parsedText
            : int.MaxValue;
    }

    private static string StatusClass(string status) => status switch {
        "Active" => "s-active",
        "Completed" => "s-completed",
        "Failed" => "s-failed",
        "Suspended" => "s-suspended",
        _ => "s-none"
    };

    private static string StatusStampClass(string status) => status.ToLowerInvariant() switch {
        "approved" or "processed" or "completed" => "approved",
        "rejected" or "failed" => "rejected",
        "pending" or "running" => "pending",
        _ => "other"
    };

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

    private static string FmtDate(string dt) =>
        TryParse(dt, out var d)
            ? d.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture)
            : dt;

    private static string FmtTime(string dt) =>
        TryParse(dt, out var d)
            ? d.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture)
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
