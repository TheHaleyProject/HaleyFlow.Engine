
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class ResolvedTransition {
        public int FromStateId { get; set; }
        public int ToStateId { get; set; }
        public int EventId { get; set; }   // events.id
        public string? GuardKey { get; set; } // only if you reintroduce it later
    }
}
