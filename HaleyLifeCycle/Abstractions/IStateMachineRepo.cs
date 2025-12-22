using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Haley.Abstractions {

    public interface IStateMachineRepo {
        (IAdapterGateway agw, string adapterKey) AdapterGatewayInfo { get; }
        #region Primitives
        Task<IFeedback<bool>> Exists(LifeCycleEntity entity, LifeCycleKey key);
        Task<IFeedback<bool>> Delete(LifeCycleEntity entity, LifeCycleKey key);
        Task<IFeedback<Dictionary<string, object>>> Get(LifeCycleEntity entity, LifeCycleKey key);
        Task<IFeedback<List<Dictionary<string, object>>>> List(LifeCycleEntity entity, LifeCycleKey? scope = null, int skip = 0, int limit = 200);

        #endregion

        #region Base (Environment, Category, Definition, DefVersion)
        Task<IFeedback<Dictionary<string, object>>> UpsertEnvironment(string displayName, int code);
        Task<IFeedback<Dictionary<string, object>>> UpsertCategory(string displayName);
        Task<IFeedback<Dictionary<string, object>>> UpsertDefinition(string displayName, string description, int envId);
        Task<IFeedback<bool>> UpdateDefinitionDescription(long definitionId, string newDescription);

        Task<IFeedback<Dictionary<string, object>>> UpsertDefinitionVersion(long parentDefinitionId, int version, string jsonData);

        // key:
        //  - LifeCycleKey(Id, definitionId) OR LifeCycleKey(Composite, envCode, defName)
        Task<IFeedback<Dictionary<string, object>>> GetLatestDefinitionVersion(LifeCycleKey key);
        #endregion

        #region Core (State, Event, Transition)
        Task<IFeedback<Dictionary<string, object>>> UpsertState(string displayName, int defVersion, LifeCycleStateFlag flags, int category = 0, int? timeoutMinutes = null, int timeoutMode = 0, int? timeoutEventId = null);

        // requiredFlags=IsInitial => single=true (LIMIT 1)
        // requiredFlags=IsFinal/IsSystem/IsError => single=false
        Task<IFeedback<List<Dictionary<string, object>>>> GetStateByFlags(int defVersion, LifeCycleStateFlag requiredFlags);
        Task<IFeedback<Dictionary<string, object>>> UpsertEvent(string displayName, int code, int defVersion);
        Task<IFeedback<Dictionary<string, object>>> UpsertTransition(int fromState, int toState, int eventId, int defVersion);
        Task<IFeedback<Dictionary<string, object>>> GetTransition(int fromState, int eventId, int defVersion);
        Task<IFeedback<List<Dictionary<string, object>>>> ListOutgoingTransition(int fromState, int defVersion);

        #endregion

        #region Instance
        Task<IFeedback<Dictionary<string, object>>> UpsertInstance(int defVersion, int currentState, int? lastEvent, string externalRef, LifeCycleInstanceFlag flags);

        // key: LifeCycleKey(Id, instanceId) OR LifeCycleKey(Guid, instanceGuid)
        Task<IFeedback<bool>> UpdateInstanceState(LifeCycleKey key, int newState, int lastEvent, LifeCycleInstanceFlag flags);
        Task<IFeedback<bool>> MarkInstanceCompleted(LifeCycleKey key);

        Task<IFeedback<long>> AppendTransitionLog(long instanceId, int fromState, int toState, int eventId, string? actor = null, string? metadata = null);

        // key: LifeCycleKey(Id, logId)
        Task<IFeedback<Dictionary<string, object>>> GetTransitionLog(LifeCycleKey key);
        Task<IFeedback<Dictionary<string, object>>> GetLatestTransitionLog(long instanceId);

        // filter:
        //  - LifeCycleKey(Id, instanceId) OR LifeCycleKey(Composite, fromStateId, toStateId) OR LifeCycleKey(Composite, fromUtc, toUtc)
        Task<IFeedback<List<Dictionary<string, object>>>> GetTransitionLogList(LifeCycleKey filter, int skip = 0, int limit = 200);

        #endregion

        #region Acknowledgement
        Task<IFeedback<Dictionary<string, object>>> InsertAck(long transitionLogId, int consumer, int ackStatus = 1, string? messageId = null);
        // key: LifeCycleKey(Name, messageId) OR LifeCycleKey(Composite, transitionLogId, consumer)
        Task<IFeedback<bool>> MarkAck(string messageId, int ackStatus);
        Task<IFeedback<List<Dictionary<string, object>>>> GetAck(LifeCycleAckFetchMode mode, int maxRetry, int retryAfterMinutes, int skip = 0, int limit = 200);
        Task<IFeedback<bool>> RetryAck(long ackId);

        #endregion

        #region Maintenance
        Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesWithExpiredTimeouts(int maxBatchSize);
        Task<IFeedback<bool>> PurgeOldLogs(int daysToKeep);
        Task<IFeedback<bool>> RebuildIndexes();
        #endregion
    }
}
