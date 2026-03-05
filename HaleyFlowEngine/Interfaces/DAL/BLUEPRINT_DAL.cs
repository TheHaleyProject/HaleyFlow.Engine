using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IBlueprintReadDAL {
        Task<DbRow?> GetLatestDefVersionByEnvCodeAndDefNameAsync(int envCode, string defName, DbExecutionLoad load = default);
        Task<DbRow?> GetLatestDefVersionByEnvNameAndDefNameAsync(string envName, string defName, DbExecutionLoad load = default);
        Task<DbRow?> GetLatestDefVersionByDefinitionGuidAsync(string defGuid, DbExecutionLoad load = default);
        Task<DbRow?> GetLatestDefVersionByLineFromDefVersionIdAsync(long defVersionId, DbExecutionLoad load = default);
        Task<DbRow?> GetLatestDefVersionByLineFromDefVersionGuidAsync(string defVersionGuid, DbExecutionLoad load = default);
        Task<int?> GetNextDefVersionNumberByEnvCodeAndDefNameAsync(int envCode, string defName, DbExecutionLoad load = default);
        Task<DbRow?> GetDefVersionByParentAndHashAsync(int definitionId, string hash, DbExecutionLoad load = default);

        Task<DbRow?> GetDefVersionByIdAsync(long defVersionId, DbExecutionLoad load = default);

        Task<DbRows> ListStatesAsync(long defVersionId, DbExecutionLoad load = default);
        Task<DbRows> ListEventsAsync(long defVersionId, DbExecutionLoad load = default);
        Task<DbRows> ListTransitionsAsync(long defVersionId, DbExecutionLoad load = default);

        Task<DbRow?> GetPolicyByIdAsync(long policyId, DbExecutionLoad load = default);
        Task<DbRow?> GetPolicyByHashAsync(string hash, DbExecutionLoad load = default);

        public Task<DbRow?> GetPolicyForDefinition(long definitionId, DbExecutionLoad load = default);
        public Task<DbRow?> GetPolicyForDefVersion(long defVersionId, DbExecutionLoad load = default);
    }
    internal interface IBlueprintWriteDAL {
        Task<int> EnsureEnvironmentByCodeAsync(int envCode, string envDisplayName, DbExecutionLoad load = default);
        Task<int> EnsureDefinitionByEnvIdAsync(int envId, string defDisplayName, string? description, DbExecutionLoad load = default);
        Task<long> InsertDefVersionAsync(int definitionId, int version, string data, string hash, DbExecutionLoad load = default);
        Task<int> EnsureCategoryByNameAsync(string displayName, DbExecutionLoad load = default);
        Task<int> InsertEventAsync(long defVersionId, string displayName, int code, DbExecutionLoad load = default);
        Task<int> InsertStateAsync(long defVersionId, int categoryId, string displayName, uint flags, DbExecutionLoad load = default);
        Task<int> InsertTransitionAsync(long defVersionId, int fromStateId, int toStateId, int eventId, DbExecutionLoad load = default);
        Task<long> EnsurePolicyByHashAsync(string hash, string content, DbExecutionLoad load = default);
        Task<int> AttachPolicyToDefinitionByEnvCodeAndDefNameAsync(int envCode, string defName, long policyId, DbExecutionLoad load = default);
        Task<int> DeleteByPolicyIdAsync(long policyId, DbExecutionLoad load = default);
        Task<int> InsertAsync(long policyId, string stateName, int duration, int mode, int? eventCode, DbExecutionLoad load = default);
        Task<DbRows> ListByPolicyIdAsync(long policyId, DbExecutionLoad load = default);
    }
    internal interface IConsumerDAL {
        Task<int?> GetIdByEnvIdAndGuidAsync(int envId, string consumerGuid, DbExecutionLoad load = default);
        Task<int?> GetIdAliveByEnvIdAndGuidAsync(int envId, string consumerGuid, int ttlSeconds, DbExecutionLoad load = default);
        Task<int> UpsertBeatByEnvIdAndGuidAsync(int envId, string consumerGuid, DbExecutionLoad load = default);
        Task<int> UpdateBeatByIdAsync(int consumerId, DbExecutionLoad load = default);
        Task<IReadOnlyList<int>> ListAliveIdsByEnvIdAsync(int envId, int ttlSeconds, DbExecutionLoad load = default);
        Task<int> IsAliveByIdAsync(int consumerId, int ttlSeconds, DbExecutionLoad load = default);
        Task<int> IsAliveByEnvIdAndGuidAsync(int envId, string consumerGuid, int ttlSeconds, DbExecutionLoad load = default);
        Task<DateTime?> GetLastBeatByEnvIdAndGuidAsync(int envId, string consumerGuid, DbExecutionLoad load = default);
        Task<int> EnsureByEnvIdAndGuidReturnIdAsync(int envId, string consumerGuid, DbExecutionLoad load = default);
    }
}
