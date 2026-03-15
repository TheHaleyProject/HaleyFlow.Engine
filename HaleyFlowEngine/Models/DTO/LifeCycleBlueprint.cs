using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class LifeCycleBlueprint {
        public long DefVersionId { get; set; }
        public int Version { get; set; }
        public long DefinitionId { get; set; }
        public int EnvCode { get; set; }
        public string DefName { get; set; }
        public IReadOnlyDictionary<long, StateDef> StatesById { get; set; }
        public IReadOnlyDictionary<long, EventDef> EventsById { get; set; }
        public IReadOnlyDictionary<string, EventDef> EventsByName { get; set; }
        public IReadOnlyDictionary<int, EventDef> EventsByCode { get; set; }
        public IReadOnlyDictionary<Tuple<long, int>, TransitionDef> Transitions { get; set; }
        public long InitialStateId { get; set; }
        public LifeCycleBlueprint() { }
    }
}
