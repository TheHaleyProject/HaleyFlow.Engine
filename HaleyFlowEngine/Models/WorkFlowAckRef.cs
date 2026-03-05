using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class WorkFlowAckRef : IWorkFlowAckRef {
        public long AckId { get; set; }
        public string AckGuid { get; set; }
    }
}
