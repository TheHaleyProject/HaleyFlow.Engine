using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;

namespace WFE.Test.UseCases;

/// <summary>
/// Shared interactive wrapper behavior for hook-driven confirmation flows.
/// Concrete wrappers provide only definition identity and route handlers.
/// </summary>
public abstract class InteractiveHookWrapperBase : LifeCycleWrapper {
    private static readonly SemaphoreSlim PromptLock = new(1, 1);

    protected readonly UseCaseRuntimeOptions Options;

    protected abstract string DefinitionName { get; }

    protected InteractiveHookWrapperBase(IWorkFlowEngineAccessor engineAccessor, UseCaseRuntimeOptions options) : base(engineAccessor) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    protected static string PickEvent(string? preferred, string fallback)
        => !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;

    protected virtual Task BeforeDecisionAsync(ILifeCycleHookEvent evt, string decisionMessage, CancellationToken ct)
        => Task.CompletedTask;

    protected virtual Task AfterDecisionAsync(ILifeCycleHookEvent evt, bool yes, bool hasFailurePath, CancellationToken ct)
        => Task.CompletedTask;

    protected async Task<AckOutcome> ConfirmAndTriggerAsync(
        ILifeCycleHookEvent evt,
        ConsumerContext ctx,
        string decisionMessage,
        string yesEventCode,
        string? noEventCode,
        BusinessActionExecutionMode executionMode = BusinessActionExecutionMode.SkipIfCompleted) {
        var timeout = Options.ConfirmationTimeout;
        var timeoutText = timeout > TimeSpan.Zero ? $", auto-yes in {timeout.TotalSeconds:0}s" : string.Empty;
        var prompt = $"[CONSUMER] {decisionMessage} entity={evt.EntityId} route={evt.Route} (Y/N, Enter=Y{timeoutText})";

        await BeforeDecisionAsync(evt, decisionMessage, ctx.CancellationToken);

        bool yes;
        if (!string.IsNullOrWhiteSpace(noEventCode) && int.TryParse(yesEventCode, out var actionCode)) {
            var decision = false;
            var execution = await ExecuteBusinessActionAsync(ctx, evt.DefinitionId, evt.EntityId, actionCode,
                async token => {
                    decision = await AskConfirmationAsync(prompt, ConsoleKey.Y, timeout, token);
                    return new {
                        decision = decision ? "yes" : "no",
                        route = evt.Route,
                        entity = evt.EntityId,
                        question = decisionMessage
                    };
                },
                executionMode);

            yes = execution.Executed
                ? decision
                : ReadDecisionFromResultJson(execution.ResultJson, defaultValue: true);

            if (!execution.Executed) {
                Console.WriteLine($"[CONSUMER] route={evt.Route} -> business action already completed; reusing prior decision {(yes ? "YES" : "NO")}.");
            }
        } else {
            yes = await AskConfirmationAsync(prompt, ConsoleKey.Y, timeout, ctx.CancellationToken);
        }

        var hasFailurePath = !string.IsNullOrWhiteSpace(noEventCode);
        await AfterDecisionAsync(evt, yes, hasFailurePath, ctx.CancellationToken);

        if (yes) {
            return await TriggerNextAsync(evt, ctx, yesEventCode);
        }

        if (hasFailurePath) {
            return await TriggerNextAsync(evt, ctx, noEventCode!);
        }

        Console.WriteLine($"[CONSUMER] route={evt.Route} -> user chose NO, leaving ack as RETRY.");
        return AckOutcome.Retry;
    }

    protected async Task<AckOutcome> TriggerNextAsync(ILifeCycleHookEvent evt, ConsumerContext ctx, string nextEventCode) {
        if (!int.TryParse(nextEventCode, out _)) {
            Console.WriteLine($"[CONSUMER] route={evt.Route} has non-numeric next event '{nextEventCode}', skipping trigger.");
            return AckOutcome.Processed;
        }

        var request = new LifeCycleTriggerRequest {
            EnvCode = Options.EnvCode,
            DefName = DefinitionName,
            EntityId = evt.EntityId,
            Event = nextEventCode,
            Actor = "wfe.test.consumer",
            AckRequired = true,
            Payload = new Dictionary<string, object> {
                ["fromRoute"] = evt.Route,
                ["consumerWfId"] = ctx.WfId,
                ["consumerId"] = ctx.ConsumerId
            }
        };

        var engine = await EngineAccessor.GetEngineAsync(ctx.CancellationToken);
        var result = await engine.TriggerAsync(request, ctx.CancellationToken);
        Console.WriteLine($"[CONSUMER] route={evt.Route} -> event={nextEventCode} applied={result.Applied} reason={result.Reason}");
        return AckOutcome.Processed;
    }

    protected static async Task<bool> AskConfirmationAsync(
        string message,
        ConsoleKey defaultKey,
        TimeSpan timeout,
        CancellationToken ct) {
        await PromptLock.WaitAsync(ct);
        try {
            return await ReadConfirmationWithTimeoutAsync(message, defaultKey, timeout, ct);
        } finally {
            PromptLock.Release();
        }
    }

    private static async Task<bool> ReadConfirmationWithTimeoutAsync(
        string message,
        ConsoleKey defaultKey,
        TimeSpan timeout,
        CancellationToken ct) {
        Console.WriteLine();
        Console.WriteLine(message);

        var timeoutEnabled = timeout > TimeSpan.Zero;
        var deadline = DateTime.UtcNow + timeout;

        while (true) {
            ct.ThrowIfCancellationRequested();

            bool hasKey;
            try {
                hasKey = Console.KeyAvailable;
            } catch (InvalidOperationException) {
                return defaultKey == ConsoleKey.Y;
            }

            if (hasKey) {
                var key = Console.ReadKey(intercept: true);
                Console.WriteLine();

                if (key.Key == ConsoleKey.Enter) return defaultKey == ConsoleKey.Y;
                if (key.Key == ConsoleKey.Y) return true;
                if (key.Key == ConsoleKey.N) return false;

                Console.WriteLine("Wrong input. Accepted inputs: Y, N, Enter.");
                continue;
            }

            if (timeoutEnabled && DateTime.UtcNow >= deadline) {
                Console.WriteLine($"[PROMPT] No input in {timeout.TotalSeconds:0}s. Defaulting to YES.");
                return defaultKey == ConsoleKey.Y;
            }

            await Task.Delay(100, ct);
        }
    }
}
