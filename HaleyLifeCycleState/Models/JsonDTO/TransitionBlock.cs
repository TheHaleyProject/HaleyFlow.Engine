using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class TransitionBlock {
        public int Event { get; set; }          // event code (ex: 1005)
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string? Guard { get; set; }      // optional (if you keep future branching)
    }
}
