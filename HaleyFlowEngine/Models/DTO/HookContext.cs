using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class HookContext : RuleContext {
        public DateTimeOffset? NotBefore { get; set; }
        public DateTimeOffset? Deadline { get; set; }
    }
}
