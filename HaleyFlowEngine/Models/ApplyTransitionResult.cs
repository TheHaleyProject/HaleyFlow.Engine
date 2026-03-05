using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class ApplyTransitionResult {
        public bool Applied { get; set; }
        public long? LifeCycleId { get; set; }
        public long FromStateId { get; set; }
        public long ToStateId { get; set; }
        public long EventId { get; set; }
        public int EventCode { get; set; }
        public string EventName { get; set; }
        public string Reason { get; set; }
        public ApplyTransitionResult() { }
    }
}
