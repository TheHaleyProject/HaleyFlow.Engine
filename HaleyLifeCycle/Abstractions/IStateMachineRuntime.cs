using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    public interface IStateMachineRuntime {
        event Func<TransitionOccurred, Task>? TransitionRaised;
        event Func<StateMachineError, Task>? ErrorRaised;
        event Func<TimeoutNotification, Task>? TimeoutRaised;
        event Func<StateMachineNotice, Task>? NoticeRaised;
        Task<IFeedback<StateMachineNotice>> TriggerAsync(LifeCycleKey instanceKey, int eventCode, string? actor = null, string? comment = null, object? context = null);
        Task<IFeedback<StateMachineNotice>> TriggerAsync(LifeCycleKey instanceKey, string eventName, string? actor = null, string? comment = null, object? context = null);


        // Instance lifecycle
        Task<LifeCycleInstance?> GetInstanceWithTransitionAsync(LifeCycleKey instanceKey);
        Task<LifeCycleInstance?> GetInstanceAsync(LifeCycleKey instanceKey);
        Task<bool> InitializeAsync(LifeCycleKey instanceKey, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active);
        Task<LifeCycleState> GetCurrentStateAsync(LifeCycleKey instanceKey);


        // Validation / helpers
        //Task<bool> CanTransitionAsync(int definitionVersion, int fromStateId, int eventCode); //No longer applicable, as app only raises event and doesn't care about the transition itself.
        Task<bool> IsInitialStateAsync(int definitionVersion, int stateId);
        Task<bool> IsFinalStateAsync(int definitionVersion, int stateId);

        Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(LifeCycleKey instanceKey, int skip = 0, int limit = 200);

        // Admin - override
        Task<bool> ForceUpdateStateAsync(LifeCycleKey instanceKey, int newStateId, string? actor = null, string? metadata = null);

        // Definition import
        Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromJsonAsync(string json, string environmentName = "default", int envCode = 0);
        Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromFileAsync(string filePath,string environmentName = "default", int envCode =0);

        // Ack pass-through
        Task<IFeedback<Dictionary<string, object>>> InsertAck(long transitionLogId, int consumer, LifeCycleAckStatus status = LifeCycleAckStatus.Pending, string? messageId = null);
        Task<IFeedback<bool>> MarkAck(string messageId, LifeCycleAckStatus status);
    }
}
