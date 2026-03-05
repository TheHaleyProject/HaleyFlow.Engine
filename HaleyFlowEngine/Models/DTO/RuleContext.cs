using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal class RuleContext {
        public IReadOnlyList<LifeCycleParamItem>? Params { get; set; }
        public string? OnSuccessEvent { get; set; }
        public string? OnFailureEvent { get; set; }
    }
}
