
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class ResolvedDefinition {
        public long DefinitionId { get; set; }
        public int DefinitionVersionId { get; set; } // def_version
    }
}
