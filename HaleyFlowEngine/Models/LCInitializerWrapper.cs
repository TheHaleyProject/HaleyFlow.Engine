using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class LCInitializerWrapper {
        public string AdapterKey { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public WorkFlowEngineOptions? Options { get; set; }
        public LCInitializerWrapper() { }   
    }
}
