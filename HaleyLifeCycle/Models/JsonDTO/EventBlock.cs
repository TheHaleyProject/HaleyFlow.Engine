using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class EventBlock {
        public int Code { get; set; }           // stable contract
        public string Name { get; set; } = "";  // stable name
        public override string ToString() {
            return $@"{Code} - {Name}";
        }
    }
}
