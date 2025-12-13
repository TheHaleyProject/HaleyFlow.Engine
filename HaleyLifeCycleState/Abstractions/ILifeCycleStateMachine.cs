using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    public interface ILifeCycleStateMachine {
        event Func<TransitionOccurred, Task>? TransitionRaised;

        Task<bool> TriggerAsync(int definitionVersion, LifeCycleKey instanceKey, int eventCode, string? actor = null, string? comment = null, object? context = null);
        Task<bool> TriggerByNameAsync(int definitionVersion, LifeCycleKey instanceKey, string eventName, string? actor = null, string? comment = null, object? context = null);


        // Instance lifecycle
        Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, LifeCycleKey instanceKey);
        Task<bool> InitializeAsync(int definitionVersion, LifeCycleKey instanceKey, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active);
        Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, LifeCycleKey instanceKey);


        // Validation / helpers
        Task<bool> CanTransitionAsync(int definitionVersion, int fromStateId, int eventCode);
        Task<bool> IsInitialStateAsync(int definitionVersion, int stateId);
        Task<bool> IsFinalStateAsync(int definitionVersion, int stateId);

        Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(int definitionVersion, LifeCycleKey instanceKey, int skip = 0, int limit = 200);

        // Admin - override
        Task<bool> ForceUpdateStateAsync(int definitionVersion, LifeCycleKey instanceKey, int newStateId, string? actor = null, string? metadata = null);

        // Definition import
        Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromJsonAsync(string json, string environmentName = "default", int envCode = 0);
        Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromFileAsync(string filePath,string environmentName = "default", int envCode =0);

        // Ack pass-through
        Task<IFeedback<Dictionary<string, object>>> InsertAck(long transitionLogId, int consumer, int ackStatus = 1, string? messageId = null);
        Task<IFeedback<bool>> MarkAck(LifeCycleKey key, LifeCycleAckStatus status);
    }
}
