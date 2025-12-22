using Haley.Enums;
using System;

namespace Haley.Models {
    public class LifeCycleTransitionLog {
        public long Id { get; set; }
        public long InstanceId { get; set; }
        public int FromState { get; set; }
        public int ToState { get; set; }
        public int Event { get; set; }
        public DateTime Created { get; set; }
        public string? Metadata { get; set; } // JSON
        public string? Actor { get; set; }
    }
}