using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class AckWorkItem {
        public long AckId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public long TransitionLogId { get; set; }
        public long InstanceId { get; set; }
        public int DefinitionVersion { get; set; }
        public string ExternalRef { get; set; } = string.Empty;
        public int FromStateId { get; set; }
        public int ToStateId { get; set; }
        public int EventId { get; set; }
        public int EventCode { get; set; }
        public string EventName { get; set; } = string.Empty;
        public string Actor { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = string.Empty;
    }
}
