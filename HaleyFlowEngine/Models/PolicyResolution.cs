using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class PolicyResolution {
        public long? PolicyId { get; set; }
        public string PolicyHash { get; set; }
        public string PolicyJson { get; set; }
        public PolicyResolution() { }
    }
}
