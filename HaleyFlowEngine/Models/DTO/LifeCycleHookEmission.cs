using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class LifeCycleHookEmission : ILifeCycleHookEmission {
        public long HookId { get; set; }
        public long HookLcId { get; set; }   // hook_lc.id for this emission
        public long StateId { get; set; }
        public bool OnEntry { get; set; }
        public string Route { get; set; }
        public string OnSuccessEvent { get; set; }
        public string OnFailureEvent { get; set; }
        public DateTimeOffset? NotBefore { get; set; }
        public DateTimeOffset? Deadline { get; set; }
        public IReadOnlyList<LifeCycleParamItem>? Params { get; set; }
        public bool IsBlocking { get; set; } = true;
        public string? GroupName { get; set; }
        public int OrderSeq { get; set; } = 1;
        public int AckMode  { get; set; } = 0;
    }
}
