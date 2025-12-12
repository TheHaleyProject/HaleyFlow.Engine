using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Haley.Abstractions {

    public interface ILifeCycleStateMachine {

        // Instance identity is: (def_version + external_ref)
        Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, string externalRef);
        Task<LifeCycleInstance?> GetInstanceAsync(int definitionVersion, Guid externalRefId);

        Task InitializeAsync(int definitionVersion, string externalRef, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active);
        Task InitializeAsync(int definitionVersion, Guid externalRefId, LifeCycleInstanceFlag flags = LifeCycleInstanceFlag.Active);

        // Trigger a transition to a target state.
        // actor/metadata are stored in transition_data (not transition / transition_log).
        Task<bool> TriggerAsync(int definitionVersion, string externalRef, int toStateId, string? comment = null, string? actor = null, object? context = null, LifeCycleTransitionLogFlag flags = LifeCycleTransitionLogFlag.Manual);
        Task<bool> TriggerAsync(int definitionVersion, Guid externalRefId, int toStateId, string? comment = null, string? actor = null, object? context = null, LifeCycleTransitionLogFlag flags = LifeCycleTransitionLogFlag.Manual);

        Task<bool> ValidateTransitionAsync(int definitionVersion, int fromStateId, int toStateId);

        Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, string externalRef);
        Task<LifeCycleState> GetCurrentStateAsync(int definitionVersion, Guid externalRefId);

        Task<IReadOnlyList<LifeCycleTransitionLog?>> GetTransitionHistoryAsync(int definitionVersion, string externalRef);
        Task<IReadOnlyList<LifeCycleTransitionLog?>> GetTransitionHistoryAsync(int definitionVersion, Guid externalRefId);

        Task ForceUpdateStateAsync(int definitionVersion, string externalRef, int newStateId, LifeCycleTransitionLogFlag flags = LifeCycleTransitionLogFlag.System, string? actor = null, string? metadata = null);
        Task ForceUpdateStateAsync(int definitionVersion, Guid externalRefId, int newStateId, LifeCycleTransitionLogFlag flags = LifeCycleTransitionLogFlag.System, string? actor = null, string? metadata = null);

        Task<bool> IsFinalStateAsync(int definitionVersion, int stateId);
        Task<bool> IsInitialStateAsync(int definitionVersion, int stateId);

        Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromFileAsync(string filePath);
        Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromJsonAsync(string json);
    }
}
