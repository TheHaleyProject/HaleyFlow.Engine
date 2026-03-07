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

namespace WFE.Test.UseCases.LoanApproval {
    internal sealed class LoanApprovalUseCase : IWorkflowUseCase {
        public string Name => "loan-approval";
        public string Description => "End-to-end loan approval flow with KYC, credit, risk, and manager decision stages.";

        public async Task RunAsync(CancellationToken ct) {
            var settings = new LoanApprovalUseCaseSettings();
            var dbName = $"wfe_test_loan_approval_{DateTime.UtcNow:yyyyMMddHHmmss}";
            settings.EngineConString = settings.EngineConString.Replace("wfe_test_case_loan_approval", dbName, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"Using database: {dbName}");
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

                var serviceCollection = new ServiceCollection();
                serviceCollection.AddSingleton<IWorkFlowEngine>(engine);
                serviceCollection.AddSingleton(settings);
                serviceCollection.AddTransient<LoanApprovalWrapper>();
                var serviceProvider = serviceCollection.BuildServiceProvider();

                var consumerMaker = new WorkFlowConsumerMaker()
                    .WithConnectionString(settings.EngineConString)
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
                consumer.RegisterAssembly(typeof(LoanApprovalWrapper).Assembly);

                engine.NoticeRaised += n => {
                    Console.WriteLine($"[NOTICE:{n.Kind}] {n.Code} :: {n.Message}");
                    return Task.CompletedTask;
                };

                var definitionPath = UseCasePathResolver.Resolve("UseCases", "LoanApproval", "definition.loan_approval.json");
                var policyPath = UseCasePathResolver.Resolve("UseCases", "LoanApproval", "policy.loan_approval.json");

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
                    Event = "2000",
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
