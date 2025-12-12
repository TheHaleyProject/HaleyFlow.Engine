using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class EventBlock {
        public int Code { get; set; }           // stable contract
        public string Name { get; set; } = "";  // stable name
        public string? DisplayName { get; set; } // optional convenience (UI)
    }
}
