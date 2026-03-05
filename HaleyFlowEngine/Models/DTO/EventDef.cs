using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class EventDef {
        public int Id { get; set; }
        public int Code { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public EventDef() { }
    }
}
