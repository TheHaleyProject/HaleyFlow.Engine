using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;

namespace Haley.Abstractions {

    /// <summary>
    /// Repository abstraction for managing Lifecycle / State Machine entities.
    /// Provides CRUD, audit, and maintenance operations for definitions, states,
    /// events, transitions, instances, transition logs, acknowledgements, and categories.
    /// </summary>
    public interface ILifeCycleStateRepository {
        bool ThrowExceptions { get; }

        // ----------------------------------------------------------
        // DEFINITION & VERSION MANAGEMENT
        // ----------------------------------------------------------
        Task<IFeedback<Dictionary<string, object>>> RegisterDefinition(string displayName, string description, int env);
        Task<IFeedback<Dictionary<string, object>>> RegisterDefinitionVersion(long parentId, int version, string jsonData);
        Task<IFeedback<List<Dictionary<string, object>>>> GetAllDefinitions();
        Task<IFeedback<Dictionary<string, object>>> GetDefinitionById(long id);
        Task<IFeedback<List<Dictionary<string, object>>>> GetVersionsByDefinition(long definitionId);
        Task<IFeedback<Dictionary<string, object>>> GetLatestDefinitionVersion(long definitionId);
        Task<IFeedback<bool>> DefinitionExists(string displayName, int env);
        Task<IFeedback<bool>> UpdateDefinitionDescription(long definitionId, string newDescription);
        Task<IFeedback<bool>> DeleteDefinition(long definitionId);

        // ----------------------------------------------------------
        // STATE MANAGEMENT
        // ----------------------------------------------------------
        Task<IFeedback<Dictionary<string, object>>> RegisterState(string displayName, int defVersion, LifeCycleStateFlag flags, int category = 0, string? timeout = null, int timeoutMode = 0, int timeoutEventId = 0);
        Task<IFeedback<List<Dictionary<string, object>>>> GetStatesByVersion(int defVersion);
        Task<IFeedback<Dictionary<string, object>>> GetStateByName(int defVersion, string name);
        Task<IFeedback<Dictionary<string, object>>> GetInitialState(int defVersion);
        Task<IFeedback<Dictionary<string, object>>> GetFinalState(int defVersion);
        Task<IFeedback<bool>> UpdateStateFlags(int stateId, LifeCycleStateFlag newFlags);
        Task<IFeedback<bool>> DeleteState(int stateId);

        // ----------------------------------------------------------
        // EVENT MANAGEMENT
        // ----------------------------------------------------------
        Task<IFeedback<Dictionary<string, object>>> RegisterEvent(string displayName, int code, int defVersion);
        Task<IFeedback<List<Dictionary<string, object>>>> GetEventsByVersion(int defVersion);
        Task<IFeedback<Dictionary<string, object>>> GetEventByName(int defVersion, string name);
        Task<IFeedback<Dictionary<string, object>>> GetEventByCode(int defVersion, int code);
        Task<IFeedback<bool>> DeleteEvent(int eventId);

        // ----------------------------------------------------------
        // TRANSITION MANAGEMENT
        // ----------------------------------------------------------
        Task<IFeedback<Dictionary<string, object>>> RegisterTransition(int fromState, int toState, int eventId, int defVersion);
        Task<IFeedback<List<Dictionary<string, object>>>> GetTransitionsByVersion(int defVersion);
        Task<IFeedback<Dictionary<string, object>>> GetTransition(int fromState, int eventId, int defVersion);
        Task<IFeedback<List<Dictionary<string, object>>>> GetOutgoingTransitions(int fromState, int defVersion);
        Task<IFeedback<bool>> DeleteTransition(int transitionId);

        // ----------------------------------------------------------
        // INSTANCE MANAGEMENT
        // ----------------------------------------------------------
        Task<IFeedback<Dictionary<string, object>>> RegisterInstance(long defVersion, int currentState, int lastEvent, string externalRef, LifeCycleInstanceFlag flags);
        Task<IFeedback<Dictionary<string, object>>> GetInstanceById(long id);
        Task<IFeedback<Dictionary<string, object>>> GetInstanceByGuid(string guid);
        Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByRef(string externalRef);
        Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByState(int defVersion, int stateId);
        Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesByFlags(int defVersion, LifeCycleInstanceFlag flags);
        Task<IFeedback<bool>> UpdateInstanceState(long instanceId, int newState, int lastEvent, LifeCycleInstanceFlag flags);
        Task<IFeedback<bool>> MarkInstanceCompleted(long instanceId);
        Task<IFeedback<bool>> DeleteInstance(long instanceId);
        Task<IFeedback<bool>> UpdateInstanceStateByGuid(string guid, int newState, int lastEvent, LifeCycleInstanceFlag flags);
        Task<IFeedback<bool>> MarkInstanceCompletedByGuid(string guid);
        Task<IFeedback<bool>> DeleteInstanceByGuid(string guid);

        // ----------------------------------------------------------
        // TRANSITION LOG / AUDIT
        // ----------------------------------------------------------
        Task<IFeedback<long>> LogTransition(long instanceId, int fromState, int toState, int eventId, string? actor = null, string? metadata = null);
        Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByInstance(long instanceId);
        Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByStateChange(int fromState, int toState);
        Task<IFeedback<List<Dictionary<string, object>>>> GetLogsByDateRange(DateTime from, DateTime to);
        Task<IFeedback<Dictionary<string, object>>> GetLatestLogForInstance(long instanceId);

        // ----------------------------------------------------------
        // MAINTENANCE / UTILITIES
        // ----------------------------------------------------------
        Task<IFeedback<int>> PurgeOldLogs(int daysToKeep);
        Task<IFeedback<int>> CountInstances(int defVersion, int flagsFilter = 0);
        Task<IFeedback> RebuildIndexes();

        // ----------------------------------------------------------
        // ACKNOWLEDGEMENT LOG
        // ----------------------------------------------------------
        Task<IFeedback<Dictionary<string, object>>> Ack_Insert(long transitionLogId, int consumer, int ackStatus = 1);
        Task<IFeedback<Dictionary<string, object>>> Ack_InsertWithMessage(long transitionLogId, int consumer, string messageId, int ackStatus = 1);
        Task<IFeedback<bool>> Ack_MarkDeliveredByMessage(string messageId);
        Task<IFeedback<bool>> Ack_MarkProcessedByMessage(string messageId);
        Task<IFeedback<bool>> Ack_MarkFailedByMessage(string messageId);
        Task<IFeedback<bool>> Ack_MarkDelivered(long transitionLogId, int consumer);
        Task<IFeedback<bool>> Ack_MarkProcessed(long transitionLogId, int consumer);
        Task<IFeedback<bool>> Ack_MarkFailed(long transitionLogId, int consumer);
        Task<IFeedback<List<Dictionary<string, object>>>> Ack_GetDueForRetry(int maxRetry, int retryAfterMinutes);
        Task<IFeedback<bool>> Ack_BumpRetry(long ackId);

        // ----------------------------------------------------------
        // CATEGORY
        // ----------------------------------------------------------
        Task<IFeedback<Dictionary<string, object>>> InsertCategoryAsync(string displayName);
        Task<IFeedback<List<Dictionary<string, object>>>> GetAllCategoriesAsync();
        Task<IFeedback<Dictionary<string, object>>> GetCategoryByNameAsync(string name);
    }
}
