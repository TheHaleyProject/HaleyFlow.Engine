using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IPolicyEnforcer {
        Task<PolicyResolution> ResolvePolicyAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default);
        Task<IReadOnlyList<ILifeCycleHookEmission>> EmitHooksAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default, string? resolvedPolicyJson = null);
        Task<PolicyResolution> ResolvePolicyAsync(long definitionId, DbExecutionLoad load = default);
        Task<PolicyResolution> ResolvePolicyByIdAsync(long policyId, DbExecutionLoad load = default);
        RuleContext ResolveRuleContextFromJson(string policyJson, StateDef toState, EventDef? viaEvent, CancellationToken ct = default);
        HookContext ResolveHookContextFromJson(string policyJson, StateDef toState, EventDef? viaEvent, string hookCode, CancellationToken ct = default);
    }
}
