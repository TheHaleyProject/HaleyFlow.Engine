using Haley;
using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Services;
using Haley.Log;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("Workflow test app (v0.3) started.");

// -------------------------
// 0) Settings
// -------------------------
const int EnvCode = 1;
const string EnvDisplayName = "dev";
const long ConsumerId = 10;              // this console app acts as a consumer
const bool AckRequired = true;

// Files (keep these next to exe, or run from repo root)
var defPath = FindFile("vendor_registration.json");
var polPath = FindFile("vendor_registration_policies.json");


// OPTIONAL: auto-drive workflow by completing hooks.
// You can remove this entirely if you only want to see first trigger stored.
Dictionary<string, int> HookAutoResponses = new(StringComparer.OrdinalIgnoreCase) {
    // CheckVendorRegistered -> choose "VendorNotRegistered" (1002)
    ["APP.REG.CHECK.VENDOR_REGISTERED"] = 1002,

    // CheckRecentSubmission -> choose "NoRecentRequest" (1004)
    ["APP.REG.CHECK.RECENT_SUBMISSION"] = 1004,

    // PendingPQValidation -> choose "CompanyValid" (1006)
    ["APP.REG.PQ.VALIDATION.REQUIRED"] = 1006,

    // VendorCreation -> choose "VendorCreated" (1007)
    ["APP.REG.VENDOR_CREATE"] = 1007
};
// -------------------------
// 1) Create DAL (YOU FILL THIS)
// -------------------------
IWorkFlowDAL dal = await CreateDalOrThrow();

// -------------------------
// 2) Build engine (BlueprintImporter is REQUIRED in options)
// -------------------------

var opt = new WorkFlowEngineOptions {
    DefaultConsumerId = ConsumerId,
    MonitorInterval = TimeSpan.FromSeconds(10),
    MonitorPageSize = 200,

    // IMPORTANT: WorkFlowEngine constructor throws if BlueprintImporter is null
    MonitorConsumers = new long[] { ConsumerId }
};

await using var engine = new WorkFlowEngine(dal, opt);

// Optional: start monitor loop (you can also call RunMonitorOnceAsync manually)
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// -------------------------
// 3) Subscribe to engine events
// -------------------------
engine.NoticeRaised += n => {
    Console.WriteLine($"[NOTICE:{n.Kind}] {n.Code} :: {n.Message}");
    if (n.Exception != null) Console.WriteLine(n.Exception);
    return Task.CompletedTask;
};

// Auto-ACK + optional auto-flow driver for hook events
engine.EventRaised += async evt => {
    if (evt.Kind == LifeCycleEventKind.Transition) {
        var t = (ILifeCycleTransitionEvent)evt;
        Console.WriteLine($"[TRN] ext={t.ExternalRef} {t.FromStateId}->{t.ToStateId} ev={t.EventCode} {t.EventName} ack={t.AckGuid}");

        if (t.AckRequired && !string.IsNullOrWhiteSpace(t.AckGuid))
            await engine.AckAsync(t.ConsumerId, t.AckGuid, AckOutcome.Processed, "transition-processed", ct: CancellationToken.None);
        return;
    }

    if (evt.Kind == LifeCycleEventKind.Hook) {
        var h = (ILifeCycleHookEvent)evt;
        Console.WriteLine($"[HOOK] ext={h.ExternalRef} code={h.HookCode} onSuccess={h.OnSuccessEvent} onFailure={h.OnFailureEvent} ack={h.AckGuid}");

        if (h.AckRequired && !string.IsNullOrWhiteSpace(h.AckGuid))
            await engine.AckAsync(h.ConsumerId, h.AckGuid, AckOutcome.Processed, "hook-processed", ct: CancellationToken.None);

        // OPTIONAL: auto-complete the workflow by triggering the next event based on hook code.
        // This simulates your application processing the hook and calling TriggerAsync again.
        if (HookAutoResponses.TryGetValue(h.HookCode ?? string.Empty, out var nextEventCode)) {
            var followUp = new LifeCycleTriggerRequest {
                EnvCode = EnvCode,
                DefName = "VendorRegistration",
                ExternalRef = h.ExternalRef,
                Event = nextEventCode.ToString(),
                Actor = "console-auto",
                RequestId = Guid.NewGuid().ToString(),
                AckRequired = AckRequired,
                Payload = new Dictionary<string, object> { ["fromHook"] = h.HookCode ?? "" }
            };

            var r = await engine.TriggerAsync(followUp, CancellationToken.None);
            Console.WriteLine($"    -> auto-triggered {nextEventCode} applied={r.Applied} lc={r.LifeCycleId}");
        }
    }
};

// -------------------------
// 4) Import definition + policy
// -------------------------
var defJson = await File.ReadAllTextAsync(defPath, cts.Token);
var polJson = await File.ReadAllTextAsync(polPath, cts.Token);

// Definition name from your JSON sample
const string DefName = "VendorRegistration";

// Policy importer (current implementation) requires a root-level "defName".
// Your vendor_registration_policies.json DOES NOT have it; it has for.definition.
// So we inject defName (safe: PolicyEnforcer uses "routes", and ignores extra fields).
polJson = EnsurePolicyHasDefName(polJson, DefName);

//Rememer, we can either import definition/policy if the json is present or we can just read the latest defversion/policy info from the db
var defVersionId = await engine.BlueprintImporter.ImportDefinitionJsonAsync(EnvCode, EnvDisplayName, defJson, cts.Token);
Console.WriteLine($"Imported definition: defVersionId={defVersionId}");

var policyId = await engine.BlueprintImporter.ImportPolicyJsonAsync(EnvCode, EnvDisplayName, polJson, cts.Token);
Console.WriteLine($"Imported/attached policy: policyId={policyId}");

// Invalidate blueprint cache so latest import is used immediately
await engine.InvalidateAsync(EnvCode, DefName, cts.Token);

// Quick DB verification via DAL reads
var states = await dal.Blueprint.ListStatesAsync(defVersionId);
var events = await dal.Blueprint.ListEventsAsync(defVersionId);
var trans = await dal.Blueprint.ListTransitionsAsync(defVersionId);
Console.WriteLine($"DB check: states={states.Count} events={events.Count} transitions={trans.Count}");

// -------------------------
// 5) Trigger: create instance + first event
// -------------------------
var externalRef = Guid.NewGuid().ToString();          // your request/external reference
var first = new LifeCycleTriggerRequest {
    EnvCode = EnvCode,
    DefName = DefName,
    ExternalRef = externalRef,
    Event = "1000",                                   // AutoStart
    Actor = "console",
    RequestId = Guid.NewGuid().ToString(),
    AckRequired = AckRequired,
    Payload = new Dictionary<string, object> {
        ["demo"] = true,
        ["startedAtUtc"] = DateTime.UtcNow.ToString("O")
    }
};

var firstRes = await engine.TriggerAsync(first, cts.Token);
Console.WriteLine($"Trigger(1000) applied={firstRes.Applied} instanceId={firstRes.InstanceId} lcId={firstRes.LifeCycleId} {firstRes.FromState}->{firstRes.ToState}");

// Optional: show timeline JSON
var timeline = await dal.LifeCycle.GetTimelineJsonByInstanceIdAsync(firstRes.InstanceId);
if (!string.IsNullOrWhiteSpace(timeline))
    Console.WriteLine($"Timeline JSON:\n{timeline}");

// -------------------------
// 6) Interactive loop
// -------------------------
Console.WriteLine("\nType an event code (e.g., 1002) and press Enter. Type 'q' to quit.");
while (!cts.IsCancellationRequested) {
    var line = Console.ReadLine();
    if (line == null) continue;
    if (string.Equals(line.Trim(), "q", StringComparison.OrdinalIgnoreCase)) break;

    if (!int.TryParse(line.Trim(), out var code)) continue;

    var req = new LifeCycleTriggerRequest {
        EnvCode = EnvCode,
        DefName = DefName,
        ExternalRef = externalRef,
        Event = code.ToString(),
        Actor = "console-manual",
        RequestId = Guid.NewGuid().ToString(),
        AckRequired = AckRequired,
        Payload = new Dictionary<string, object> { ["manual"] = true }
    };

    var r = await engine.TriggerAsync(req, cts.Token);
    Console.WriteLine($"Trigger({code}) applied={r.Applied} lcId={r.LifeCycleId} {r.FromState}->{r.ToState}");
}

Console.WriteLine("Done.");

// -------------------------
// Helpers
// -------------------------

static string FindFile(string name) {
    var p1 = Path.Combine(AppContext.BaseDirectory, name);
    if (File.Exists(p1)) return p1;

    var p2 = Path.Combine(Directory.GetCurrentDirectory(), name);
    if (File.Exists(p2)) return p2;

    throw new FileNotFoundException($"File not found: {name}");
}

// Your policy JSON sample has no root defName, but ImportPolicyJsonAsync requires it.
static string EnsurePolicyHasDefName(string policyJson, string defName) {
    var node = JsonNode.Parse(policyJson) as JsonObject;
    if (node == null) return policyJson;

    // If already present, keep it.
    if (node.ContainsKey("defName") || node.ContainsKey("definitionName") || node.ContainsKey("name") || node.ContainsKey("displayName"))
        return policyJson;

    node["defName"] = defName;
    return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}



// YOU implement this to return your concrete DAL.
// Keep it throwing so you don't accidentally run with null.
static async Task<IWorkFlowDAL> CreateDalOrThrow() {
    var constring = $"server=127.0.0.1;port=3306;user=root;password=admin@456$;database=testlcs;Allow User Variables=true;";
    //var response = await LifeCycleInitializer.InitializeAsync(new AdapterGateway(), "lcstate");
    var agw = new AdapterGateway() { LogQueryInConsole = true };
    var response = await LifeCycleInitializer.InitializeAsyncWithConString(agw, constring);
    if (!response.Status) throw new ArgumentException("Unable to initialize the database for the lifecycle state machine");
    return new MariaWorkFlowDAL(agw, response.Result);
}
