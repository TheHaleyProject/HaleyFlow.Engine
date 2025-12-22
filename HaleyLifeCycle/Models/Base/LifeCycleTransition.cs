using Haley.Enums;
using System;

namespace Haley.Models {
    public sealed class LifeCycleTransition {
        public int Id { get; set; }
        public int DefinitionVersion { get; set; }
        public int FromState { get; set; }
        public int ToState { get; set; }
        public int Event { get; set; } // event id (not code)
        public DateTime Created { get; set; }
    }
}
