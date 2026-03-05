using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface ILifeCycleHookEmission {
        long HookId { get; }
        long StateId { get; }
        bool OnEntry { get; }
        string Route { get; }
        string? OnSuccessEvent { get; }
        string? OnFailureEvent { get; }
        DateTimeOffset? NotBefore { get; }
        DateTimeOffset? Deadline { get; }
        IReadOnlyDictionary<string, object?>? Payload { get; } // ephemeral (NOT stored)
        IReadOnlyList<LifeCycleParamItem>? Params { get; }
        bool IsBlocking { get; }
        string? GroupName { get; }
    }
}
