using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class TransitionOccurred {
        public string MessageId { get; set; }
        public long TransitionLogId { get; set; }
        public long InstanceId { get; set; }
        public int DefinitionVersion { get; set; }
        public string ExternalRef { get; set; } = "";

        public int FromStateId { get; set; }
        public int ToStateId { get; set; }

        public int EventId { get; set; }     // db id
        public int EventCode { get; set; }   // stable contract for app
        public string EventName { get; set; } = "";

        public string? Actor { get; set; }
        public string? Metadata { get; set; }
        public DateTime Created { get; set; }  = DateTime.UtcNow;
    }
}
