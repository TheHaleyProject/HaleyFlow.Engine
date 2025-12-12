using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    public interface ILifeCycleStateMachine {
        event Func<TransitionOccurred, Task>? TransitionRaised;
        Task<bool> TriggerAsync(int definitionVersion, string externalRef, int eventCode, string? actor = null, string? comment = null, object? context = null);
        Task<bool> TriggerAsync(int definitionVersion, Guid externalRefId, int eventCode, string? actor = null, string? comment = null, object? context = null);
        Task<bool> TriggerByNameAsync(int definitionVersion, string externalRef, string eventName, string? actor = null, string? comment = null, object? context = null);
        Task<bool> TriggerByNameAsync(int definitionVersion, Guid externalRefId, string eventName, string? actor = null, string? comment = null, object? context = null);

        Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, string externalRef);
        Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, Guid externalRefId);

        Task InitializeAsync(int definitionVersion, string externalRef, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active);
        Task InitializeAsync(int definitionVersion, Guid externalRefId, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active);

        Task<bool> ValidateTransitionAsync(int definitionVersion, int fromStateId, int eventCode);

        Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, string externalRef);
        Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, Guid externalRefId);

        Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(int definitionVersion, string externalRef);
        Task<IReadOnlyList<LifeCycleTransitionLog>> GetTransitionHistoryAsync(int definitionVersion, Guid externalRefId);

        Task ForceUpdateStateAsync(int definitionVersion, string externalRef, int newStateId, string? actor = null, string? metadata = null);
        Task ForceUpdateStateAsync(int definitionVersion, Guid externalRefId, int newStateId, string? actor = null, string? metadata = null);

        Task<bool> IsFinalStateAsync(int definitionVersion, int stateId);
        Task<bool> IsInitialStateAsync(int definitionVersion, int stateId);

        Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromFileAsync(string filePath);
        Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromJsonAsync(string json);

        Task<IFeedback<Dictionary<string, object>>> Ack_Insert(long transitionLogId, int consumer, int ackStatus = 1);
        Task<IFeedback<Dictionary<string, object>>> Ack_InsertWithMessage(long transitionLogId, int consumer, string messageId, int ackStatus = 1);

        Task<IFeedback<bool>> Ack_MarkByMessageAsync(string messageId, LifeCycleAckStatus status);
        Task<IFeedback<bool>> Ack_MarkByTransitionAsync(long transitionLogId, int consumer, LifeCycleAckStatus status);
    }
}
