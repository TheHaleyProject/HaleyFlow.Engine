using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class StateMachineError {
        public object? Reference { get; init; }
        public string Operation { get; init; } = string.Empty;   // "TriggerAsync", "RaiseTransitionAsync", etc.
        public Exception Exception { get; init; } = new Exception("Unknown error");
        public object Data { get; set; }
        public DateTime TimeStamp { get; init; } = DateTime.UtcNow;
    }
}
