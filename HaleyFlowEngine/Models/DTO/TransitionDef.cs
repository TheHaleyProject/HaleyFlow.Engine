using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class TransitionDef {
        public long FromStateId { get; set; }
        public long ToStateId { get; set; }
        public long EventId { get; set; }
        public uint Flags { get; set; }
        public TransitionDef() { }
    }
}
