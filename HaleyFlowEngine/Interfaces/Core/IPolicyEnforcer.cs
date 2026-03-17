using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IPolicyEnforcer {
        Task<PolicyResolution> ResolvePolicyAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default);
        // policy replaces the old string? resolvedPolicyJson parameter — callers pass the full PolicyResolution
        // so EmitHooksAsync can use its policyId to hit the parsed-policy cache instead of re-parsing JSON.
        Task<IReadOnlyList<ILifeCycleHookEmission>> EmitHooksAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default, PolicyResolution? policy = null);
        Task<PolicyResolution> ResolvePolicyAsync(long definitionId, DbExecutionLoad load = default);
        Task<PolicyResolution> ResolvePolicyByIdAsync(long policyId, DbExecutionLoad load = default);
        // policyId = 0 means "no cache available" (AckManager path). WorkFlowEngine passes pr.PolicyId for a cache hit.
        RuleContext ResolveRuleContextFromJson(string policyJson, StateDef toState, EventDef? viaEvent, CancellationToken ct = default, long policyId = 0);
        HookContext ResolveHookContextFromJson(string policyJson, StateDef toState, EventDef? viaEvent, string hookCode, CancellationToken ct = default, long policyId = 0);
        void ClearPolicyCache();
    }
}
