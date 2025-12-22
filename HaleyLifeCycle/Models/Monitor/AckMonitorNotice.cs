using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class AckMonitorNotice {
        public required AckWorkItem Work { get; init; }
        public required LifeCycleAckStatus Status { get; init; }
        public required string Reason { get; init; }
        public required DateTime CreatedUtc { get; init; }
        public required DateTime ModifiedUtc { get; init; }
        public required TimeSpan Age { get; init; }
        public int Consumer { get; init; }
        public int RetryCount { get; init; }
    }
}
