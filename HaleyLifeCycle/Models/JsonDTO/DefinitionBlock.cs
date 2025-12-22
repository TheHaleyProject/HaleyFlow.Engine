using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class DefinitionBlock {
        public string Name { get; set; } = string.Empty;
        public int Version { get; set; }
        public string? Description { get; set; }
        public string? Environment { get; set; }
        public int EnvironmentCode { get; set; } = 0;
    }
}
