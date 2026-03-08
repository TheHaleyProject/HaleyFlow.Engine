using Haley.Abstractions;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace WFE.Test.UseCases.LoanApproval {
    internal sealed class LoanApprovalUseCase : IWorkflowUseCase {
        public string Name => "loan-approval";
        public string Description => "End-to-end loan approval flow with KYC, credit, risk, and manager decision stages.";

        public async Task RunAsync(CancellationToken ct) {
            var settings = new LoanApprovalUseCaseSettings();
            var agw = new AdapterGateway();
            long resolvedConsumerId = 0;

            var engineMaker = new WorkFlowEngineMaker().WithAdapterKey(settings.ENGINE_DBNAME);
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
                serviceCollection.AddTransient<LoanApprovalWrapper>();
                var serviceProvider = serviceCollection.BuildServiceProvider();

                var consumerMaker = new WorkFlowConsumerMaker()
                    .WithAdapterKey(settings.CONSUMER_DBNAME)
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
                consumer.RegisterAssembly(typeof(LoanApprovalWrapper).Assembly);

                // Engine notices are relayed through InProcessEngineProxy → consumer.NoticeRaised,
                // so a single subscription here captures both engine and consumer failures.
                consumer.NoticeRaised += n => {
                    Console.WriteLine($"[NOTICE:{n.Kind}] {n.Code} :: {n.Message}" +
                        (n.Exception != null ? $" ex={n.Exception.GetType().Name}: {n.Exception.Message}" : string.Empty));
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

                var wrapperDriver = serviceProvider.GetRequiredService<LoanApprovalWrapper>();
                var createdEntities = new List<string>();

                if (settings.RandomEntityCount > 0) {
                    Console.WriteLine(
                        $"[DRIVER] Random entity mode enabled. count={settings.RandomEntityCount}, interval={settings.RandomEntityInterval.TotalSeconds:0}s");

                    for (var i = 1; i <= settings.RandomEntityCount; i++) {
                        ct.ThrowIfCancellationRequested();
                        Console.WriteLine($"\n[DRIVER] Attempt {i}/{settings.RandomEntityCount}");

                        var createdEntityId = await wrapperDriver.TryStartRandomEntityAsync(Name, ct);
                        if (!string.IsNullOrWhiteSpace(createdEntityId)) {
                            createdEntities.Add(createdEntityId);
                        }

                        if (i < settings.RandomEntityCount && settings.RandomEntityInterval > TimeSpan.Zero) {
                            await Task.Delay(settings.RandomEntityInterval, ct);
                        }
                    }
                } else {
                    Console.WriteLine("[DRIVER] RandomEntityCount is 0. Skipping entity creation.");
                }

                if (settings.WaitAfterTrigger > TimeSpan.Zero) {
                    Console.WriteLine($"[DRIVER] Waiting {settings.WaitAfterTrigger.TotalSeconds:0}s before timeline snapshot...");
                    await Task.Delay(settings.WaitAfterTrigger, ct);
                }

                Console.WriteLine($"\n[DRIVER] Created entity count: {createdEntities.Count}");
                foreach (var entityId in createdEntities) {
                    await PrintEntityTimelineAsync(engine, settings, entityId, ct);
                }

                if (settings.KeepAliveAfterRun) {
                    await WaitForExitAsync(settings, ct);
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

        private static async Task PrintEntityTimelineAsync(
            IWorkFlowEngine engine,
            LoanApprovalUseCaseSettings settings,
            string entityId,
            CancellationToken ct) {
            var key = new LifeCycleInstanceKey {
                EnvCode = settings.EnvCode,
                DefName = settings.DefName,
                EntityId = entityId
            };

            var data = await engine.GetInstanceDataAsync(key, ct);
            if (data == null) {
                Console.WriteLine($"[SNAPSHOT] entity={entityId} instance data not found.");
                return;
            }

            Console.WriteLine($"\n[SNAPSHOT] entity={entityId} instance={data.InstanceGuid} currentStateId={data.CurrentStateId}");
            var timeline = await engine.GetTimelineJsonAsync(new LifeCycleInstanceKey { InstanceGuid = data.InstanceGuid }, ct);
            Console.WriteLine(timeline ?? "(null)");
        }

        private static async Task WaitForExitAsync(LoanApprovalUseCaseSettings settings, CancellationToken ct) {
            var exitCommand = string.IsNullOrWhiteSpace(settings.ExitCommand) ? "exit" : settings.ExitCommand.Trim();
            Console.WriteLine($"\n[DRIVER] Use-case is still running. Type '{exitCommand}' (or press Enter) to stop.");

            while (!ct.IsCancellationRequested) {
                var line = Console.ReadLine();
                if (line == null) {
                    await Task.Delay(200, ct);
                    continue;
                }

                var input = line.Trim();
                if (input.Length == 0 || string.Equals(input, exitCommand, StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine("[DRIVER] Exit command received. Stopping services...");
                    return;
                }

                Console.WriteLine($"[DRIVER] Unknown input '{input}'. Type '{exitCommand}' or press Enter.");
            }
        }
    }
}
