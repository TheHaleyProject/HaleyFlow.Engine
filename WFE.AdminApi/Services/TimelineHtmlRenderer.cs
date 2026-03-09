using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace WFE.AdminApi.Services;

/// <summary>
/// Converts a timeline JSON string (from IWorkFlowEngine.GetTimelineJsonAsync)
/// into a self-contained, browser-ready HTML page.
/// No JavaScript rendering — all data is baked in at render time.
/// </summary>
internal static class TimelineHtmlRenderer {

    private static readonly Dictionary<string, string> StatusPill =
        new(StringComparer.OrdinalIgnoreCase) {
            ["approved"] = "bg-emerald-50 text-emerald-700 border-emerald-200",
            ["rejected"] = "bg-red-50 text-red-700 border-red-200",
            ["pending"]  = "bg-amber-50 text-amber-700 border-amber-200",
        };

    public static string Render(string timelineJson, string? displayName = null) {
        using var doc = JsonDocument.Parse(timelineJson);
        var root = doc.RootElement;

        var sb = new StringBuilder(12_000);
        WriteHead(sb);

        // Outer shell: flex column filling the full viewport
        sb.Append("<div class=\"max-w-3xl w-full mx-auto px-4\" style=\"height:100vh;display:flex;flex-direction:column;padding-top:2.5rem\">\n");

        if (root.TryGetProperty("instance", out var inst) &&
            root.TryGetProperty("timeline", out var tl) &&
            tl.ValueKind == JsonValueKind.Array) {
            // Header — fixed height, never scrolls
            sb.Append("  <div style=\"flex-shrink:0\">\n");
            WriteHeader(sb, inst, tl.GetArrayLength(), displayName);
            sb.Append("  </div>\n");
            // Timeline — fills remaining space, scrolls independently
            sb.Append("  <div id=\"timeline-scroll\" style=\"flex:1;overflow-y:auto;padding-bottom:2rem\">\n");
            WriteTimeline(sb, tl);
            sb.Append("  </div>\n");
        }

        sb.Append("</div>\n</body>\n</html>");
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
  <script src="https://cdn.tailwindcss.com"></script>
  <style>
    html, body { height: 100%; overflow: hidden; font-family: -apple-system, BlinkMacSystemFont, 'Inter', 'Segoe UI', sans-serif; }
    .tl-item:not(:last-child) .tl-left::after {
      content: '';
      position: absolute;
      left: 50%;
      top: 44px;
      bottom: -32px;
      width: 2px;
      transform: translateX(-50%);
      background: linear-gradient(to bottom, #90c4e8, #d0e8f7);
    }
    .act-row:hover { background: #f0f7fd; }
    @keyframes slideIn {
      from { opacity: 0; transform: translateY(8px); }
      to   { opacity: 1; transform: translateY(0); }
    }
    .tl-item { animation: slideIn .3s ease both; }
    #timeline-scroll::-webkit-scrollbar { width: 6px; }
    #timeline-scroll::-webkit-scrollbar-track { background: transparent; }
    #timeline-scroll::-webkit-scrollbar-thumb { background: #90c4e8; border-radius: 3px; }
  </style>
</head>
<body class="bg-slate-100 text-slate-800" style="display:flex;flex-direction:column">

""");
    }

    // ── Instance Header ──────────────────────────────────────────────────────

    private static void WriteHeader(StringBuilder sb, JsonElement inst, int stepCount, string? displayName) {
        var entityId   = S(inst, "entity_id");
        var guid       = S(inst, "guid");
        var label      = !string.IsNullOrWhiteSpace(displayName) ? displayName : entityId;
        var defName    = S(inst, "def_name");
        var defVersion = S(inst, "def_version");
        var curState   = S(inst, "current_state");
        var lastEvent  = SplitCamel(S(inst, "last_event"));
        var created    = FmtFull(S(inst, "created"));
        var modified   = FmtFull(S(inst, "modified"));
        var totalDur   = Dur(S(inst, "created"), S(inst, "modified"));

        sb.Append($"""
  <div class="rounded-2xl overflow-hidden shadow-sm mb-8" style="border:1px solid #b3d0eb;background:#f0f7fd">
    <div class="px-6 pt-5 pb-5">
      <div class="flex items-start justify-between gap-4 flex-wrap">
        <div class="min-w-0">
          <div class="flex items-center gap-2 mb-2 flex-wrap">
            <span class="text-xs uppercase tracking-widest font-bold" style="color:#0078d4">{E(defName)}</span>
            <span class="text-xs px-2 py-0.5 rounded border font-medium" style="color:#0078d4;border-color:#b3d0eb;background:#e1f0fa">v{E(defVersion)}</span>
          </div>
          <p class="font-bold text-lg tracking-tight break-all" style="color:#1a1a1a">{E(label)}</p>
          <p class="text-xs mt-0.5 font-mono break-all" style="color:#5a9dc0">{E(entityId)}</p>
          <p class="text-xs mt-0.5 font-mono" style="color:#8ab8d8">{E(guid)}</p>
        </div>
        <div class="flex-shrink-0 text-right">
          <div class="text-xs mb-1.5 uppercase tracking-wide font-medium" style="color:#0078d4">Current State</div>
          <div class="inline-block px-3 py-1.5 rounded-lg" style="background:#0078d4">
            <span class="font-semibold text-sm text-white">{E(curState)}</span>
          </div>
        </div>
      </div>
    </div>
    <div class="px-6 py-3 flex flex-wrap gap-5 text-xs" style="border-top:1px solid #b3d0eb;background:#f0f7fd">
      <div class="text-slate-500"><span class="font-semibold text-slate-700 block">Last Event</span>{E(lastEvent)}</div>
      <div class="text-slate-500"><span class="font-semibold text-slate-700 block">Started</span>{E(created)}</div>
      <div class="text-slate-500"><span class="font-semibold text-slate-700 block">Completed</span>{E(modified)}</div>
      <div class="text-slate-500"><span class="font-semibold text-slate-700 block">Total Duration</span>{E(totalDur)}</div>
      <div class="text-slate-500"><span class="font-semibold text-slate-700 block">Transitions</span>{stepCount} steps</div>
    </div>
  </div>

""");
    }

    // ── Timeline ─────────────────────────────────────────────────────────────

    private static void WriteTimeline(StringBuilder sb, JsonElement tl) {
        sb.Append("  <div class=\"space-y-0\">\n");
        var items = tl.EnumerateArray().ToList();
        var prevWasTerminal = false;
        for (var i = 0; i < items.Count; i++) {
            WriteItem(sb, items[i], i,
                isLast:          i == items.Count - 1,
                next:            i + 1 < items.Count ? (JsonElement?)items[i + 1] : null,
                prevWasTerminal: prevWasTerminal);
            prevWasTerminal = B(items[i], "is_terminal");
        }
        sb.Append("  </div>\n");
    }

    private static void WriteItem(StringBuilder sb, JsonElement item, int idx, bool isLast, JsonElement? next, bool prevWasTerminal) {
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
        var actCount   = acts.ValueKind == JsonValueKind.Array ? acts.GetArrayLength() : 0;
        var delay      = (idx * 0.07).ToString("F2", CultureInfo.InvariantCulture);

        // Badge shown inside the banner — only when there's something to say
        var bannerBadge = (isInitial, isTerminal, isReopen) switch {
            (true, _, _) =>
                """<span class="text-xs font-semibold px-2.5 py-1 rounded text-white" style="background:#0f766e">Workflow Started</span>""",
            (_, true, _) =>
                """<span class="text-xs font-semibold px-2.5 py-1 rounded text-white" style="background:#1d4ed8">Workflow Ended</span>""",
            (_, _, true) =>
                """<span class="text-xs font-semibold px-2.5 py-1 rounded text-white" style="background:#92400e">Reopened</span>""",
            _ => string.Empty
        };

        sb.Append($"""
    <div class="tl-item flex gap-4" style="animation-delay:{delay}s">
      <div class="tl-left relative flex flex-col items-center flex-shrink-0 w-10">
        <div class="w-10 h-10 rounded-full shadow-sm flex items-center justify-center z-10 flex-shrink-0 text-white font-semibold text-sm" style="background:#0078d4">
          {idx + 1}
        </div>
      </div>
      <div class="flex-1 pb-8 min-w-0">
        <div class="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden hover:shadow-md transition-shadow duration-200">

          <!-- Transition banner -->
          <div class="px-4 py-3" style="background:#fefce8;border-bottom:1px solid #fde68a">
            <div class="flex items-center gap-3">
              <div class="flex-1 min-w-0">
                <div class="text-xs uppercase tracking-wider mb-0.5" style="color:#92400e">From</div>
                <div class="font-semibold text-sm leading-tight" style="color:#1c1917">{E(fromState)}</div>
              </div>
              <svg class="w-4 h-4 flex-shrink-0" style="color:#d97706" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7l5 5-5 5M6 12h12"/>
              </svg>
              <div class="flex-1 min-w-0 text-right">
                <div class="text-xs uppercase tracking-wider mb-0.5" style="color:#92400e">To</div>
                <div class="font-semibold text-sm leading-tight" style="color:#1c1917">{E(toState)}</div>
              </div>
            </div>
            {(bannerBadge.Length > 0 ? $"            <div class=\"mt-2 pt-2 flex\">{bannerBadge}</div>" : "")}
          </div>

          <!-- Event meta -->
          <div class="px-4 py-3 border-b border-slate-100">
            <div class="flex items-start justify-between gap-3 flex-wrap">
              <div>
                <div class="flex items-center gap-2 flex-wrap">
                  <span class="font-semibold text-slate-800 text-sm">{E(evtName)}</span>
                  <span class="text-xs px-1.5 py-0.5 rounded font-mono" style="background:#e1f0fa;color:#0078d4;border:1px solid #b3d0eb">evt:{E(evtCode)}</span>
                  <span class="text-xs bg-slate-50 text-slate-400 border border-slate-200 px-1.5 py-0.5 rounded font-mono">lc:{E(lcId)}</span>
                </div>
                <code class="text-xs text-slate-400 mt-1.5 block">{E(actor)}</code>
              </div>
              <div class="text-right flex-shrink-0">
                <div class="text-xs text-slate-400">{E(created)}</div>

""");

        if (actCount > 0) {
            sb.Append($"                <div class=\"text-xs mt-0.5\" style=\"color:#0078d4\">{actCount} activit{(actCount > 1 ? "ies" : "y")}</div>\n");
        }

        sb.Append("""
              </div>
            </div>
          </div>
          <div class="pt-3">

""");

        WriteActivities(sb, acts);

        sb.Append("          </div>\n        </div>\n");

        if (!isLast && next.HasValue) {
            var gap = Dur(S(item, "created"), S(next.Value, "created"));
            sb.Append($"        <div class=\"mt-3 ml-1\"><span class=\"text-xs text-slate-400\">{E(gap)} to next step</span></div>\n");
        }

        sb.Append("      </div>\n    </div>\n");
    }

    private static void WriteActivities(StringBuilder sb, JsonElement acts) {
        if (acts.ValueKind != JsonValueKind.Array || acts.GetArrayLength() == 0) {
            sb.Append("            <p class=\"text-xs text-slate-400 italic px-4 pb-4\">No activities recorded.</p>\n");
            return;
        }

        sb.Append("            <div class=\"px-4 pb-4 space-y-2\">\n");
        foreach (var act in acts.EnumerateArray()) {
            var activity = S(act, "activity");
            var actorId  = S(act, "actor_id");
            var status   = S(act, "status");
            var dur      = Dur(S(act, "created"), S(act, "modified"));
            var pill     = StatusPill.TryGetValue(status, out var p) ? p : "bg-slate-50 text-slate-600 border-slate-200";

            sb.Append($"""
              <div class="act-row flex items-center gap-3 border border-slate-100 rounded-lg px-3 py-2.5 transition-colors">
                <div class="flex-1 min-w-0">
                  <code class="text-xs text-slate-700 block truncate">{E(activity)}</code>
                  <span class="text-xs text-slate-400 font-mono mt-0.5 block truncate">{E(actorId)}</span>
                </div>
                <div class="flex flex-col items-end gap-1 flex-shrink-0">
                  <span class="text-xs px-2 py-0.5 rounded-full border font-medium {pill}">{E(status)}</span>
                  <span class="text-xs text-slate-400">{E(dur)}</span>
                </div>
              </div>

""");
        }
        sb.Append("            </div>\n");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
        // Return raw "<1m" — E() will encode the < when embedding in HTML
        return m < 1 ? "<1m" : m < 60 ? $"{m}m" : $"{m / 60}h {m % 60}m";
    }

    private static bool TryParse(string dt, out DateTime result) =>
        DateTime.TryParse(dt, null,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
}
