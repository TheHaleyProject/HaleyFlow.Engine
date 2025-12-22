using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class InstanceMonitorNotice {
        public required long InstanceId { get; init; }
        public required int DefinitionVersion { get; init; }
        public required string ExternalRef { get; init; }
        public required int CurrentStateId { get; init; }
        public required string CurrentStateName { get; init; }
        public required int? TimeoutMinutes { get; init; }
        public required int TimeoutMode { get; init; } // 0=once, 1=repeat
        public required DateTime StateLastChangedUtc { get; init; }
        public required TimeSpan StuckDuration { get; init; }
        public int? TimeoutEventCode { get; init; }
    }
}
