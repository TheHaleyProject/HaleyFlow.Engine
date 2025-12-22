using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class LifeCycleDefinitionJson {
        public DefinitionBlock Definition { get; set; } = new();
        public List<StateBlock> States { get; set; } = new();
        public List<EventBlock> Events { get; set; } = new();
        public List<TransitionBlock> Transitions { get; set; } = new();
    }
}
