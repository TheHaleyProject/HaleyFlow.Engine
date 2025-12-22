using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class StateBlock {
        public string Name { get; set; } = string.Empty;
        public bool IsInitial { get; set; }
        public bool IsFinal { get; set; }
        public bool IsSystem { get; set; }
        public bool IsError { get; set; }
        public string Category { get; set; } = "business";
        public string? Timeout { get; set; } // "P7D" etc
        public string? TimeoutMode { get; set; } // "once" or "repeat"
        public int? TimeoutEvent { get; set; } // event code (ex: 1002)
        public List<string>? Flags { get; set; } // optional, only if you still want state flags in JSON
        public override string ToString() {
            return $@"{Name} - {Category}";
        }
    }
}
