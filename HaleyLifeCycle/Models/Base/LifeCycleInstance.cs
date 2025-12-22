using Haley.Enums;
using System;

namespace Haley.Models {
    public class LifeCycleInstance {
        public long Id { get; set; }
        public Guid Guid { get; set; }
        public int LastEvent { get; set; }
        public int CurrentState { get; set; }
        public string? ExternalRef { get; set; }   // e.g. "wf-182"
        public LifeCycleInstanceFlag Flags { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public int DefinitionVersion { get; set; }
    }
}
