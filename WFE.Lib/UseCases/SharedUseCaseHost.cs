using Haley.Abstractions;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WFE.Test.UseCases.ChangeRequest;
using WFE.Test.UseCases.LoanApproval;
using WFE.Test.UseCases.PaperlessReview;
using WFE.Test.UseCases.VendorRegistration;

namespace WFE.Test.UseCases {
    public static class SharedUseCaseHost {
        private sealed record UseCaseProfile(
            string Key,
            string Description,
            string Folder,
            string DefinitionFile,
            string PolicyFile,
            string DefName,
            string StartEvent);

        private static readonly IReadOnlyDictionary<string, UseCaseProfile> Profiles =
            new Dictionary<string, UseCaseProfile>(StringComparer.OrdinalIgnoreCase) {
                ["vendor-registration"] = new(
                    "vendor-registration",
                    "Vendor registration flow with prechecks, validation, and vendor creation.",
                    "VendorRegistration",
                    "definition.vendor_registration.json",
                    "policy.vendor_registration.json",
                    VendorRegistrationUseCaseSettings.DefinitionNameConst,
                    "1000"),
                ["loan-approval"] = new(
                    "loan-approval",
                    "End-to-end loan approval flow with KYC, credit, risk, and manager decision stages.",
                    "LoanApproval",
                    "definition.loan_approval.json",
                    "policy.loan_approval.json",
                    LoanApprovalUseCaseSettings.DefinitionNameConst,
                    "2000"),
                ["paperless-review"] = new(
                    "paperless-review",
                    "Consultant drawing/model review with department reviews, grading, and resubmission loop.",
                    "PaperlessReview",
                    "definition.paperless_review.json",
                    "policy.paperless_review.json",
                    PaperlessReviewUseCaseSettings.DefinitionNameConst,
                    "3000"),
                ["change-request"] = new(
                    "change-request",
                    "Project change request process with impact/cost/schedule checks and a rework loop.",
                    "ChangeRequest",
                    "definition.change_request.json",
                    "policy.change_request.json",
                    ChangeRequestUseCaseSettings.DefinitionNameConst,
                    "4000")
            };

        private static readonly SemaphoreSlim StartLock = new(1, 1);
        private static readonly SemaphoreSlim StopLock = new(1, 1);

        private static UseSettingsBase? _settings;
        private static IWorkFlowEngine? _engine;
        private static IWorkFlowConsumerService? _consumer;
        private static ServiceProvider? _serviceProvider;
        private static long _resolvedConsumerId;
        private static bool _started;

        public static bool IsStarted => _started;

        public static IReadOnlyList<string> GetUseCaseKeys()
            => Profiles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

        public static bool TryGetDescription(string useCaseName, out string description) {
            description = string.Empty;
            if (!Profiles.TryGetValue(useCaseName, out var profile)) return false;
            description = profile.Description;
            return true;
        }

        public static string GetDescriptionOrDefault(string useCaseName)
            => TryGetDescription(useCaseName, out var description) ? description : string.Empty;

        public static async Task RunAsync(string useCaseName, CancellationToken ct) {
            EnsureUseCaseExists(useCaseName);
            await EnsureStartedAsync(ct);

            Console.WriteLine($"[HOST] Runtime is active. useCase={useCaseName}");
            if (_settings?.KeepAliveAfterRun == true) {
                await WaitForExitAsync(_settings, ct);
                await StopAsync(CancellationToken.None);
            }
        }

        public static async Task EnsureStartedAsync(CancellationToken ct) {
            if (_started) return;

            await StartLock.WaitAsync(ct);
            try {
                if (_started) return;

                var settings = new UseSettingsBase();
                var changeSettings = ApplyBaseSettings(settings, new ChangeRequestUseCaseSettings());
                var loanSettings = ApplyBaseSettings(settings, new LoanApprovalUseCaseSettings());
                var paperlessSettings = ApplyBaseSettings(settings, new PaperlessReviewUseCaseSettings());
                var vendorSettings = ApplyBaseSettings(settings, new VendorRegistrationUseCaseSettings());

                var agw = new AdapterGateway();

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
                        if (_resolvedConsumerId <= 0) return Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>());
                        return Task.FromResult<IReadOnlyList<long>>(new[] { _resolvedConsumerId });
                    }
                };

                var engine = await engineMaker.Build(agw);

                await engine.RegisterEnvironmentAsync(settings.EnvCode, settings.EnvDisplayName, ct);
                _resolvedConsumerId = await engine.RegisterConsumerAsync(settings.EnvCode, settings.ConsumerGuid, ct);

                var serviceCollection = new ServiceCollection();
                serviceCollection.AddSingleton<IWorkFlowEngine>(engine);
                serviceCollection.AddSingleton(changeSettings);
                serviceCollection.AddSingleton(loanSettings);
                serviceCollection.AddSingleton(paperlessSettings);
                serviceCollection.AddSingleton(vendorSettings);
                serviceCollection.AddTransient<ChangeRequestWrapper>();
                serviceCollection.AddTransient<LoanApprovalWrapper>();
                serviceCollection.AddTransient<PaperlessReviewWrapper>();
                serviceCollection.AddTransient<VendorRegistrationWrapper>();
                var provider = serviceCollection.BuildServiceProvider();

                var feed = new InProcessEngineProxy(engine);
                var consumerMaker = new WorkFlowConsumerMaker()
                    .WithAdapterKey(settings.CONSUMER_DBNAME)
                    .WithProvider(provider);

                consumerMaker.EngineProxy = feed;
                consumerMaker.Options = new ConsumerServiceOptions {
                    EnvCode = settings.EnvCode,
                    ConsumerGuid = settings.ConsumerGuid,
                    BatchSize = settings.ConsumerBatchSize,
                    PollInterval = settings.ConsumerPollInterval,
                    HeartbeatInterval = settings.ConsumerHeartbeatInterval
                };

                var consumer = await consumerMaker.Build(agw);
                consumer.RegisterAssembly(typeof(ChangeRequestWrapper).Assembly);

                consumer.NoticeRaised += n => {
                    Console.WriteLine($"[NOTICE:{n.Kind}] {n.Code} :: {n.Message}" +
                        (n.Exception != null ? $" ex={n.Exception.GetType().Name}: {n.Exception.Message}" : string.Empty));
                    return Task.CompletedTask;
                };

                await ImportUseCasesAsync(engine, settings, ct);

                await engine.StartMonitorAsync(ct);
                await consumer.StartAsync(ct);

                _settings = settings;
                _engine = engine;
                _consumer = consumer;
                _serviceProvider = provider;
                _started = true;
            } catch {
                await UnsafeStopAsync();
                throw;
            } finally {
                StartLock.Release();
            }
        }

        public static async Task<string?> CreateEntityAsync(string useCaseName, CancellationToken ct) {
            if (!Profiles.TryGetValue(useCaseName, out var profile)) {
                throw new ArgumentException($"Unknown use-case '{useCaseName}'.", nameof(useCaseName));
            }

            await EnsureStartedAsync(ct);

            var entityId = Guid.NewGuid().ToString("N");
            var trigger = await _engine!.TriggerAsync(new LifeCycleTriggerRequest {
                EnvCode = _settings!.EnvCode,
                DefName = profile.DefName,
                EntityId = entityId,
                Event = profile.StartEvent,
                Actor = "wfe.lib.host",
                AckRequired = true,
                Payload = new Dictionary<string, object> {
                    ["source"] = "WFE.Lib",
                    ["useCase"] = profile.Key
                },
                Metadata = $"shared-host;{profile.Key}"
            }, ct);

            Console.WriteLine($"[HOST] useCase={profile.Key} entity={entityId} event={profile.StartEvent} applied={trigger.Applied} reason={trigger.Reason}");
            return trigger.Applied ? entityId : null;
        }

        public static async Task<IReadOnlyList<string>> CreateEntitiesAsync(string useCaseName, int count, CancellationToken ct) {
            if (count < 1) return Array.Empty<string>();

            var created = new List<string>(count);
            for (var i = 0; i < count; i++) {
                ct.ThrowIfCancellationRequested();
                var entityId = await CreateEntityAsync(useCaseName, ct);
                if (!string.IsNullOrWhiteSpace(entityId)) created.Add(entityId);
            }

            return created;
        }

        public static async Task StopAsync(CancellationToken ct) {
            await StopLock.WaitAsync(ct);
            try {
                await UnsafeStopAsync();
            } finally {
                StopLock.Release();
            }
        }

        private static async Task ImportUseCasesAsync(IWorkFlowEngine engine, UseSettingsBase settings, CancellationToken ct) {
            foreach (var profile in Profiles.Values) {
                var definitionPath = UseCasePathResolver.Resolve("UseCases", profile.Folder, profile.DefinitionFile);
                var policyPath = UseCasePathResolver.Resolve("UseCases", profile.Folder, profile.PolicyFile);

                var definitionJson = await File.ReadAllTextAsync(definitionPath, ct);
                var policyJson = await File.ReadAllTextAsync(policyPath, ct);

                var defVersionId = await engine.ImportDefinitionJsonAsync(settings.EnvCode, settings.EnvDisplayName, definitionJson, ct);
                var policyId = await engine.ImportPolicyJsonAsync(settings.EnvCode, settings.EnvDisplayName, policyJson, ct);
                await engine.InvalidateAsync(settings.EnvCode, profile.DefName, ct);

                Console.WriteLine($"[BOOT] Imported use-case={profile.Key} defVersionId={defVersionId} policyId={policyId}");
            }
        }

        private static void EnsureUseCaseExists(string useCaseName) {
            if (!Profiles.ContainsKey(useCaseName)) {
                throw new ArgumentException($"Unknown use-case '{useCaseName}'.", nameof(useCaseName));
            }
        }

        private static async Task UnsafeStopAsync() {
            if (_consumer != null) {
                try { await _consumer.StopAsync(CancellationToken.None); } catch { }
            }

            if (_engine != null) {
                try { await _engine.StopMonitorAsync(CancellationToken.None); } catch { }
            }

            if (_serviceProvider != null) {
                try { _serviceProvider.Dispose(); } catch { }
            }

            if (_engine is IAsyncDisposable disposableEngine) {
                try { await disposableEngine.DisposeAsync(); } catch { }
            }

            _resolvedConsumerId = 0;
            _consumer = null;
            _engine = null;
            _serviceProvider = null;
            _settings = null;
            _started = false;
        }

        private static T ApplyBaseSettings<T>(UseSettingsBase source, T target) where T : UseSettingsBase {
            target.EnvCode = source.EnvCode;
            target.EnvDisplayName = source.EnvDisplayName;
            target.ConsumerGuid = source.ConsumerGuid;
            target.CONSUMER_DBNAME = source.CONSUMER_DBNAME;
            target.ENGINE_DBNAME = source.ENGINE_DBNAME;
            target.RandomEntityCount = source.RandomEntityCount;
            target.RandomEntityInterval = source.RandomEntityInterval;
            target.KeepAliveAfterRun = source.KeepAliveAfterRun;
            target.ExitCommand = source.ExitCommand;
            target.ConfirmationTimeout = source.ConfirmationTimeout;
            target.MonitorInterval = source.MonitorInterval;
            target.AckPendingResendAfter = source.AckPendingResendAfter;
            target.AckDeliveredResendAfter = source.AckDeliveredResendAfter;
            target.MaxRetryCount = source.MaxRetryCount;
            target.ConsumerTtlSeconds = source.ConsumerTtlSeconds;
            target.ConsumerDownRecheckSeconds = source.ConsumerDownRecheckSeconds;
            target.ConsumerBatchSize = source.ConsumerBatchSize;
            target.ConsumerPollInterval = source.ConsumerPollInterval;
            target.ConsumerHeartbeatInterval = source.ConsumerHeartbeatInterval;
            target.WaitAfterTrigger = source.WaitAfterTrigger;
            return target;
        }

        private static async Task WaitForExitAsync(UseSettingsBase settings, CancellationToken ct) {
            var exitCommand = string.IsNullOrWhiteSpace(settings.ExitCommand) ? "exit" : settings.ExitCommand.Trim();
            Console.WriteLine($"\n[DRIVER] Runtime is active. Type '{exitCommand}' (or press Enter) to stop.");

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
