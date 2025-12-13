using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class DefinitionBlock {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0.0";
        public string? Description { get; set; }
        public string? Environment { get; set; }
        public int EnvironmentCode { get; set; } = 0;
    }
}
