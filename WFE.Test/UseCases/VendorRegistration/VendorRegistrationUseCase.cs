using Haley;
using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.VendorRegistration {
    internal sealed class VendorRegistrationUseCase : IWorkflowUseCase {
        public string Name => "vendor-registration";
        public string Description => "Loads definition/policy JSON, starts engine + consumer in-process, and runs the registration flow.";

        public async Task RunAsync(CancellationToken ct) {
            var settings = new VendorRegistrationUseCaseSettings();
            var agw = new AdapterGateway();
            long resolvedConsumerId = 0;

            var engineMaker = new WorkFlowEngineMaker().WithAdapterKey(agw.GetDefaultKey());
            engineMaker.Options = new WorkFlowEngineOptions {
                MonitorInterval = settings.MonitorInterval,
                AckPendingResendAfter = settings.AckPendingResendAfter,
                AckDeliveredResendAfter = settings.AckDeliveredResendAfter,
                MaxRetryCount = settings.MaxRetryCount,
                ConsumerTtlSeconds = settings.ConsumerTtlSeconds,
                ConsumerDownRecheckSeconds = settings.ConsumerDownRecheckSeconds,
                ResolveConsumers = (ty, defVersionId, token) => {
                    token.ThrowIfCancellationRequested();
                    if (resolvedConsumerId <= 0) return Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>());
                    return Task.FromResult<IReadOnlyList<long>>(new[] { resolvedConsumerId });
                }
            };

            var engine = await engineMaker.Build(agw);
            IWorkFlowConsumerService? consumer = null;

            try {
                await engine.RegisterEnvironmentAsync(settings.EnvCode, settings.EnvDisplayName, ct);
                resolvedConsumerId = await engine.RegisterConsumerAsync(settings.EnvCode, settings.ConsumerGuid, ct);

                var feed = new InProcessEngineProxy(engine);

                var serviceCollection = new ServiceCollection();
                serviceCollection.AddSingleton<IWorkFlowEngine>(engine);
                serviceCollection.AddSingleton(settings);
                serviceCollection.AddTransient<VendorRegistrationWrapper>();
                var serviceProvider = serviceCollection.BuildServiceProvider();

                var consumerMaker = new WorkFlowConsumerMaker()
                    .WithAdapterKey(agw.GetDefaultKey())
                    .WithProvider(serviceProvider);

                consumerMaker.EngineProxy = feed;
                consumerMaker.Options = new ConsumerServiceOptions {
                    EnvCode = settings.EnvCode,
                    ConsumerGuid = settings.ConsumerGuid,
                    BatchSize = settings.ConsumerBatchSize,
                    PollInterval = settings.ConsumerPollInterval,
                    HeartbeatInterval = settings.ConsumerHeartbeatInterval
                };

                consumer = await consumerMaker.Build(agw);
                consumer.RegisterAssembly(typeof(VendorRegistrationWrapper).Assembly);

                // Engine notices are relayed through InProcessEngineProxy → consumer.NoticeRaised,
                // so a single subscription here captures both engine and consumer failures.
                consumer.NoticeRaised += n => {
                    Console.WriteLine($"[NOTICE:{n.Kind}] {n.Code} :: {n.Message}" +
                        (n.Exception != null ? $" ex={n.Exception.GetType().Name}: {n.Exception.Message}" : string.Empty));
                    return Task.CompletedTask;
                };

                var definitionPath = UseCasePathResolver.Resolve("UseCases", "VendorRegistration", "definition.vendor_registration.json");
                var policyPath = UseCasePathResolver.Resolve("UseCases", "VendorRegistration", "policy.vendor_registration.json");

                var definitionJson = await File.ReadAllTextAsync(definitionPath, ct);
                var policyJson = await File.ReadAllTextAsync(policyPath, ct);

                var defVersionId = await engine.ImportDefinitionJsonAsync(settings.EnvCode, settings.EnvDisplayName, definitionJson, ct);
                var policyId = await engine.ImportPolicyJsonAsync(settings.EnvCode, settings.EnvDisplayName, policyJson, ct);
                await engine.InvalidateAsync(settings.EnvCode, settings.DefName, ct);

                Console.WriteLine($"Definition imported. defVersionId={defVersionId}");
                Console.WriteLine($"Policy imported. policyId={policyId}");

                await engine.StartMonitorAsync(ct);
                await consumer.StartAsync(ct);

                var entityId = Guid.NewGuid().ToString("N");
                var trigger = await engine.TriggerAsync(new LifeCycleTriggerRequest {
                    EnvCode = settings.EnvCode,
                    DefName = settings.DefName,
                    EntityId = entityId,
                    Event = "1000",
                    Actor = "wfe.test.runner",
                    AckRequired = true,
                    Payload = new Dictionary<string, object> {
                        ["source"] = "WFE.Test",
                        ["useCase"] = Name
                    }
                }, ct);

                Console.WriteLine($"Initial trigger applied={trigger.Applied} instanceId={trigger.InstanceId} reason={trigger.Reason}");

                await Task.Delay(settings.WaitAfterTrigger, ct);

                var key = new LifeCycleInstanceKey {
                    EnvCode = settings.EnvCode,
                    DefName = settings.DefName,
                    EntityId = entityId
                };

                var data = await engine.GetInstanceDataAsync(key, ct);
                if (data != null) {
                    Console.WriteLine($"Instance={data.InstanceGuid} currentStateId={data.CurrentStateId}");

                    var timeline = await engine.GetTimelineJsonAsync(new LifeCycleInstanceKey {
                        InstanceGuid = data.InstanceGuid
                    }, ct);

                    Console.WriteLine("\n=== TIMELINE JSON ===");
                    Console.WriteLine(timeline ?? "(null)");
                } else {
                    Console.WriteLine("Instance data not found.");
                }
            } finally {
                if (consumer != null) {
                    try { await consumer.StopAsync(CancellationToken.None); } catch { }
                }

                try { await engine.StopMonitorAsync(CancellationToken.None); } catch { }

                if (engine is IAsyncDisposable disposableEngine) {
                    try { await disposableEngine.DisposeAsync(); } catch { }
                }
            }
        }
    }
}


