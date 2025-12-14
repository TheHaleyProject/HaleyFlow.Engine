using Haley;
using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Services;
using Haley.Log;
using Haley.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;


var constring = $"server=127.0.0.1;port=4307;user=root;password=admin@456$;database=testlcs;Allow User Variables=true;";
//var response = await LifeCycleInitializer.InitializeAsync(new AdapterGateway(), "lcstate");
var agw = new AdapterGateway() { LogQueryInConsole = true };
var response = await LifeCycleInitializer.InitializeAsyncWithConString(agw, constring);
if (!response.Status) throw new ArgumentException("Unable to initialize the database for the lifecycle state machine");

var logger = LogStore.GetOrAddFileLogger("lcstatelogger", "Lifecycle state logger");
ILifeCycleStateRepository repo = new LifeCycleStateMariaDB(agw, key: response.Result, logger: logger);
ILifeCycleStateMachine sm = new LifeCycleStateMachine(repo);
var monitorOptions = new LifeCycleMonitorOptions {
    PollIntervalSeconds = 10,       // demo: fast ticks
    AckRetryAfterMinutes = 0,      // demo: pick immediately
    AckMaxRetry = 5,
    AckBatchSize = 200,
    TimeoutBatchSize = 200,
    ConsumerId = 0                // this console app is consumer #10
};

Func<AckWorkItem, Task> ackHandler = async work => {
    Console.WriteLine($"[ACK] consumer={monitorOptions.ConsumerId} msg={work.MessageId} ext={work.ExternalRef} event={work.EventCode} {work.EventName}");
    // TODO: do real work here (call your app service / coordinator)
    await Task.Delay(100);
};

var monitor = new LifeCycleStateMonitor(sm, repo, monitorOptions, ackHandler);

sm.TransitionRaised += async occurred => {
    Console.WriteLine($"[TRN] ext={occurred.ExternalRef} {occurred.FromStateId}->{occurred.ToStateId} event={occurred.EventCode} {occurred.EventName}");

    // Each consumer creates its own ack row (unique by transition_log + consumer)
    var ackInsert = await sm.MarkAck(occurred.MessageId, LifeCycleAckStatus.Delivered);
    if (!ackInsert.Status) Console.WriteLine($"[ACK-INSERT] failed: {ackInsert.Message}");
};

sm.ErrorRaised += async sme => {
    Console.WriteLine($"[ERR] {sme.Operation}: {sme.Exception.Message}");
    await Task.CompletedTask;
};

sm.TimeoutRaised += async to => {
    Console.WriteLine($"[TMO] ext={to.ExternalRef} inst={to.InstanceId}");
    await Task.CompletedTask;
};

var envName = "preq-general-dev";
var jsonPath = Path.Combine(AppContext.BaseDirectory, "vendor_registration.json");
if (!File.Exists(jsonPath)) jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "vendor_registration.json");

var json = await File.ReadAllTextAsync(jsonPath);

// If you updated the importer to accept envName, call that overload.
// Otherwise call the 1-param method.
var import = await sm.ImportDefinitionFromJsonAsync(json /*, envName */);
if (!import.Status) {
    Console.WriteLine(import.Message);
    return;
}

var defVersionId = import.Result!.DefinitionVersionId;
Console.WriteLine($"Imported def_version={defVersionId} (states={import.Result.StateCount}, events={import.Result.EventCount}, transitions={import.Result.TransitionCount})");

//Trigger sample workflow.
var externalRef = Guid.NewGuid().ToString();
var instanceKey = LifeCycleKeys.Instance(defVersionId, externalRef);

await sm.InitializeAsync(instanceKey, LifeCycleInstanceFlag.Active);

// Start monitor AFTER listener is attached
//monitor.Start();
Console.WriteLine("Monitor started.");

// Trigger vendor registration path:

var result = await sm.TriggerAsync(instanceKey, 1000, actor: "console", comment: "Submit");          // RegistrationStarted -> Submitted
result = await sm.TriggerAsync(instanceKey, 1001, actor: "console", comment: "CheckDuplicate");  // Submitted -> DuplicateCheck
result = await sm.TriggerAsync(instanceKey, 1001, actor: "console", comment: "CheckDuplicate");  // Submitted -> DuplicateCheck
result = await sm.TriggerAsync(instanceKey, 1003, actor: "console", comment: "NotRegistered");   // DuplicateCheck -> PendingValidation
result = await sm.TriggerAsync(instanceKey, 1005, actor: "console", comment: "ValidateCompany"); // PendingValidation -> CompanyValidation
result = await sm.TriggerAsync(instanceKey, 1007, actor: "console", comment: "ValidCompany");    // CompanyValidation -> Registered

// -------------------------
// 7) Interactive loop
// -------------------------
Console.WriteLine("Type an event code (e.g., 1000) and press Enter. Type 'q' to quit.");
while (true) {
    var line = Console.ReadLine();
    if (string.Equals(line, "q", StringComparison.OrdinalIgnoreCase)) break;
    if (!int.TryParse(line, out var code)) continue;

    var localRes = await sm.TriggerAsync(instanceKey, code, actor: "console", comment: $"Manual trigger {code}");
}

monitor.Dispose();
Console.WriteLine("Done.");
