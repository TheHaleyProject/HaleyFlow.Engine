using Haley;
using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace WFE.Test {
    public sealed class VendorRegistrationConsoleAppSettings {
        public int EnvCode { get; set; }
        public string EnvDisplayName { get; set; } = "dev";
        public string DefName { get; set; } = "VendorRegistration";
        public string ConsumerGuid { get; set; } = "console.vendor-registration";
        public string ConnectionString { get; set; } = "";
        public string DefinitionFileName { get; set; } = "vendor_registration.json";
        public string PolicyFileName { get; set; } = "vendor_registration_policies.json";
        public bool AckRequired { get; set; } = true;

        public TimeSpan MonitorInterval { get; set; } = TimeSpan.FromSeconds(10);
        public int MonitorPageSize { get; set; } = 200;
        public TimeSpan AckPendingResendAfter { get; set; } = TimeSpan.FromSeconds(20);
        public TimeSpan AckDeliveredResendAfter { get; set; } = TimeSpan.FromSeconds(30);

        public int ConsumerTtlSeconds { get; set; } = 30;
        public int ConsumerDownRecheckSeconds { get; set; } = 10;
        public int MaxRetryCount { get; set; } = 10;

        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    }

    public sealed class VendorRegistrationConsoleApp {
        private readonly VendorRegistrationConsoleAppSettings _s;
        private readonly ConcurrentDictionary<string, byte> _dedup = new(StringComparer.OrdinalIgnoreCase);

        // OPTIONAL: auto-drive hooks
        private readonly Dictionary<string, int> _hookAutoResponses = new(StringComparer.OrdinalIgnoreCase) {
            ["APP.REG.CHECK.VENDOR_REGISTERED"] = 1002,
            ["APP.REG.CHECK.RECENT_SUBMISSION"] = 1004,
            ["APP.REG.PQ.VALIDATION.REQUIRED"] = 1006,
            ["APP.REG.VENDOR_CREATE"] = 1007
        };

        private long _lastInstanceId;
        private string _lastExternalRef = "";

        public VendorRegistrationConsoleApp(VendorRegistrationConsoleAppSettings settings) {
            _s = settings ?? throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(_s.ConnectionString)) throw new ArgumentNullException(nameof(settings.ConnectionString));
        }



        public async Task RunAsync(CancellationToken ct) {
            Console.WriteLine("Workflow test app started.");

            var defPath = FindFile(_s.DefinitionFileName);
            var polPath = FindFile(_s.PolicyFileName);

            IWorkFlowDAL dal = await CreateDalOrThrowAsync(ct);

            // Ensure env + consumer BEFORE creating the engine (so ResolveConsumers can return a real id)
            var envId = await dal.BlueprintWrite.EnsureEnvironmentByCodeAsync(_s.EnvCode, _s.EnvDisplayName, new DbExecutionLoad(ct));
            var consumerId = await dal.Consumer.EnsureByEnvIdAndGuidReturnIdAsync(envId, _s.ConsumerGuid, new DbExecutionLoad(ct));

            var opt = new WorkFlowEngineOptions {
                MonitorInterval = _s.MonitorInterval,
                MonitorPageSize = _s.MonitorPageSize,
                AckPendingResendAfter = _s.AckPendingResendAfter,
                AckDeliveredResendAfter = _s.AckDeliveredResendAfter,
                MaxRetryCount = _s.MaxRetryCount,
                ConsumerTtlSeconds = _s.ConsumerTtlSeconds,
                ConsumerDownRecheckSeconds = _s.ConsumerDownRecheckSeconds,

                // IMPORTANT: engine captures this delegate at ctor time
                ResolveConsumers = (ty, defId, token) => {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<IReadOnlyList<long>>(new[] { Convert.ToInt64(consumerId)});
                }
            };

            await using var engine = new WorkFlowEngine(dal, opt);

            engine.NoticeRaised += n => {
                Console.WriteLine($"[NOTICE:{n.Kind}] {n.Code} :: {n.Message}");
                if (n.Exception != null) Console.WriteLine(n.Exception);
                return Task.CompletedTask;
            };

            engine.EventRaised += evt => OnEventAsync(engine, evt, ct);

           // Heartbeat loop
             var hb = RunHeartbeatAsync(engine, ct);

            //// Import definition + policy
            //var defJson = await File.ReadAllTextAsync(defPath, ct);
            //var polJson = await File.ReadAllTextAsync(polPath, ct);
            //polJson = EnsurePolicyHasDefName(polJson, _s.DefName);

            //var defVersionId = await engine.BlueprintImporter.ImportDefinitionJsonAsync(_s.EnvCode, _s.EnvDisplayName, defJson, ct);
            //Console.WriteLine($"Imported definition: defVersionId={defVersionId}");

            //var policyId = await engine.BlueprintImporter.ImportPolicyJsonAsync(_s.EnvCode, _s.EnvDisplayName, polJson, ct);
            //Console.WriteLine($"Imported/attached policy: policyId={policyId}");

            //await engine.InvalidateAsync(_s.EnvCode, _s.DefName, ct);

            //// Quick verification
            //var states = await dal.Blueprint.ListStatesAsync(defVersionId, new DbExecutionLoad(ct));
            //var events = await dal.Blueprint.ListEventsAsync(defVersionId, new DbExecutionLoad(ct));
            //var trans = await dal.Blueprint.ListTransitionsAsync(defVersionId, new DbExecutionLoad(ct));
            //Console.WriteLine($"DB check: states={states.Count} events={events.Count} transitions={trans.Count}");

            // Start monitor (dispatch resend / retries)
            await engine.StartMonitorAsync(ct);

            //// Trigger first event
            //_lastExternalRef = Guid.NewGuid().ToString("N");
            //var first = new LifeCycleTriggerRequest {
            //    EnvCode = _s.EnvCode,
            //    DefName = _s.DefName,
            //    ExternalRef = _lastExternalRef,
            //    Event = "1000",
            //    Actor = "console",
            //    RequestId = Guid.NewGuid().ToString("N"),
            //    AckRequired = _s.AckRequired,
            //    Payload = new Dictionary<string, object> {
            //        ["demo"] = true,
            //        ["startedAtUtc"] = DateTime.UtcNow.ToString("O")
            //    }
            //};

            //var firstRes = await engine.TriggerAsync(first, ct);
            //_lastInstanceId = firstRes.InstanceId;
            //Console.WriteLine($"Trigger(1000) applied={firstRes.Applied} instanceId={firstRes.InstanceId} lcId={firstRes.LifeCycleId} {firstRes.FromState}->{firstRes.ToState}");

            //Console.WriteLine("Running. Press Ctrl+C to stop.");

            try {
                while (!ct.IsCancellationRequested) await Task.Delay(250, ct);
            } catch (OperationCanceledException) { }

            try { await engine.StopMonitorAsync(CancellationToken.None); } catch { }

            if (_lastInstanceId > 0) {
                var timeline = await engine.GetTimelineJsonAsync(_lastInstanceId, CancellationToken.None);
                Console.WriteLine("\n=== TIMELINE JSON ===");
                Console.WriteLine(timeline ?? "(null)");
            }

           try { await hb; } catch { }
            Console.WriteLine("Done.");
        }

        private async Task OnEventAsync(ILifeCycleEngine engine, ILifeCycleEvent evt, CancellationToken ct) {
            // Dedup by (kind, consumer, ackGuid)
            if (!string.IsNullOrWhiteSpace(evt.AckGuid)) {
                var key = $"{evt.Kind}:{evt.ConsumerId}:{evt.AckGuid}";
                if (!_dedup.TryAdd(key, 0)) return;
            }

            if (evt.Kind == LifeCycleEventKind.Transition) {
                var t = (ILifeCycleTransitionEvent)evt;
                Console.WriteLine($"[TRN] ext={t.ExternalRef} {t.FromStateId}->{t.ToStateId} ev={t.EventCode} {t.EventName} ack={t.AckGuid}");

                if (t.AckRequired && !string.IsNullOrWhiteSpace(t.AckGuid))
                    await engine.AckAsync(t.ConsumerId, t.AckGuid, AckOutcome.Delivered, "transition-received", ct: ct);

                return;
            }

            if (evt.Kind == LifeCycleEventKind.Hook) {
                var h = (ILifeCycleHookEvent)evt;
                Console.WriteLine($"[HOOK] ext={h.ExternalRef} code={h.HookCode} onSuccess={h.OnSuccessEvent} onFailure={h.OnFailureEvent} ack={h.AckGuid}");

                // FIX: do NOT return before ACK + auto-trigger
                if (h.AckRequired && !string.IsNullOrWhiteSpace(h.AckGuid))
                    await engine.AckAsync(h.ConsumerId, h.AckGuid, AckOutcome.Delivered, "hook-processed", ct: ct);

                if (_hookAutoResponses.TryGetValue(h.HookCode ?? string.Empty, out var nextEventCode)) {
                    var followUp = new LifeCycleTriggerRequest {
                        EnvCode = _s.EnvCode,
                        DefName = _s.DefName,
                        ExternalRef = h.ExternalRef,
                        Event = nextEventCode.ToString(),
                        Actor = "console-auto",
                        RequestId = Guid.NewGuid().ToString("N"),
                        AckRequired = _s.AckRequired,
                        Payload = new Dictionary<string, object> { ["fromHook"] = h.HookCode ?? "" }
                    };

                    var r = await engine.TriggerAsync(followUp, ct);
                    _lastInstanceId = r.InstanceId;
                    Console.WriteLine($"    -> auto-triggered {nextEventCode} applied={r.Applied} lc={r.LifeCycleId}");
                }

                return;
            }

            Console.WriteLine($"[EVT] kind={evt.Kind} ext={evt.ExternalRef} ack={evt.AckGuid}");
            if (evt.AckRequired && !string.IsNullOrWhiteSpace(evt.AckGuid))
                await engine.AckAsync(evt.ConsumerId, evt.AckGuid, AckOutcome.Delivered, "event-received", ct: ct);
        }

        private async Task RunHeartbeatAsync(ILifeCycleEngine engine, CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                try {
                    await engine.BeatConsumerAsync(_s.EnvCode, _s.ConsumerGuid, ct);
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    return;
                } catch (Exception ex) {
                    Console.WriteLine($"[HEARTBEAT ERROR] {ex.Message}");
                }

                await Task.Delay(_s.HeartbeatInterval, ct);
            }
        }

        private string FindFile(string name) {
            var p1 = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(p1)) return p1;

            var p2 = Path.Combine(Directory.GetCurrentDirectory(), name);
            if (File.Exists(p2)) return p2;

            var p3 = Path.Combine("/mnt/data", name);
            if (File.Exists(p3)) return p3;

            throw new FileNotFoundException($"File not found: {name}");
        }

        private string EnsurePolicyHasDefName(string policyJson, string defName) {
            var node = JsonNode.Parse(policyJson) as JsonObject;
            if (node == null) return policyJson;

            if (node.ContainsKey("defName")) return policyJson;

            node["defName"] = defName;
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private async Task<IWorkFlowDAL> CreateDalOrThrowAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var agw = new AdapterGateway { LogQueryInConsole = false };
            var response = await LifeCycleInitializer.InitializeAsyncWithConString(agw, _s.ConnectionString);

            if (!response.Status) throw new ArgumentException("Unable to initialize the database for the lifecycle state machine");

            return new MariaWorkFlowDAL(agw, response.Result);
        }
    }
}
