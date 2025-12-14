using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class StateMachineNotice {
        public object? CorrelationId { get; set; }
        public object? Reference { get; init; }
        public string Operation { get; init; } = string.Empty;   // "TriggerAsync", "RaiseTransitionAsync", etc.
        public object Data { get; set; }
        public string Message { get; set; }
        public DateTime TimeStamp { get; init; } = DateTime.UtcNow;
        public StateMachineNoticeKind Kind { get; set; }
    }
}
