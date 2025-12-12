using Haley.Enums;
using System;

namespace Haley.Models {
    public class LifeCycleTransitionData {
        public long TransitionLog { get; set; }
        public string? Metadata { get; set; } // JSON
        public string? Actor { get; set; }
    }
}
