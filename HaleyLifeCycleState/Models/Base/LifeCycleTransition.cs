using Haley.Enums;
using System;

namespace Haley.Models {
    public class LifeCycleTransition {
        public int Id { get; set; }
        public int FromState { get; set; }
        public int ToState { get; set; }
        public int Event { get; set; }
        public DateTime Created { get; set; }
        public int DefinitionVersion { get; set; }
    }
}
