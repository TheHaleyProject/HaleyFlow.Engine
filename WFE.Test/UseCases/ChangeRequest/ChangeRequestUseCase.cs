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

namespace WFE.Test.UseCases.ChangeRequest {
    internal sealed class ChangeRequestUseCase : IWorkflowUseCase {
        public string Name => "change-request";
        public string Description => "Project change request process with impact/cost/schedule checks and a rework loop.";

        public async Task RunAsync(CancellationToken ct) {
            var settings = new ChangeRequestUseCaseSettings();
            var agw = new AdapterGateway();
            long resolvedConsumerId = 0;

            var engineMaker = new WorkFlowEngineMaker().WithConnectionString(settings.EngineConString);
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

                var feed = new InProcessEventFeed(engine);

                var serviceCollection = new ServiceCollection(); //Start a new service colletion
                serviceCollection.AddSingleton<IWorkFlowEngine>(engine);
                serviceCollection.AddSingleton(settings);
                serviceCollection.AddTransient<ChangeRequestWrapper>();
                var serviceProvider = serviceCollection.BuildServiceProvider();

                var consumerMaker = new WorkFlowConsumerMaker()
                    .WithConnectionString(settings.ConsumerConString)
                    .WithProvider(serviceProvider);

                consumerMaker.EventFeed = feed;
                consumerMaker.Options = new ConsumerServiceOptions {
                    EnvCode = settings.EnvCode,
                    ConsumerGuid = settings.ConsumerGuid,
                    BatchSize = settings.ConsumerBatchSize,
                    PollInterval = settings.ConsumerPollInterval,
                    HeartbeatInterval = settings.ConsumerHeartbeatInterval
                };

                consumer = await consumerMaker.Build(agw);
                consumer.RegisterAssembly(typeof(ChangeRequestWrapper).Assembly); //only for in memory cache loading.

                engine.NoticeRaised += n => {
                    Console.WriteLine($"[NOTICE:{n.Kind}] {n.Code} :: {n.Message}");
                    return Task.CompletedTask;
                };

                var definitionPath = UseCasePathResolver.Resolve("UseCases", "ChangeRequest", "definition.change_request.json");
                var policyPath = UseCasePathResolver.Resolve("UseCases", "ChangeRequest", "policy.change_request.json");

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
                    Event = "4000",
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
