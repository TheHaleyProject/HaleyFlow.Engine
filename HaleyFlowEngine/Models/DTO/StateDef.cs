using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class StateDef {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public uint Flags { get; set; }
        public bool IsInitial { get; set; }
        public StateDef() { }
    }
}
