using Haley.Enums;
using System;

namespace Haley.Models {
    public sealed class LifeCycleState {
        public int Id { get; set; }
        public int DefinitionVersion { get; set; } // def_version
        public string DisplayName { get; set; } = "";
        public string Name { get; set; } = "";
        public int Category { get; set; }          // category id
        public LifeCycleStateFlag Flags { get; set; }
        public int? Timeout { get; set; }          // seconds (recommended) OR string if you chose ISO in DB
        public int TimeoutMode { get; set; }       // 0=once,1=repeat
        public int? TimeoutEvent { get; set; }     // event id
        public DateTime Created { get; set; }
    }
}
