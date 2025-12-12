
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class ResolvedEvent {
        public int EventId { get; set; }     // events.id
        public int Code { get; set; }        // events.code
        public string Name { get; set; } = "";
    }
}
