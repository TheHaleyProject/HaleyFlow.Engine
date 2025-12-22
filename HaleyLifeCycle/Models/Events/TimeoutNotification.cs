using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class TimeoutNotification {
        public string ExternalRef { get; init; } = string.Empty;
        public long InstanceId { get; init; }
        public int CurrentState { get; init; }
        public long TransitionLogId { get; init; }
        public string? Metadata { get; init; }
        public DateTime DueBy { get; set; }
        public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
    }
}
