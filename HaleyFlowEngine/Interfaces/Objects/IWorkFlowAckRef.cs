using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Enums;
using Haley.Models;

namespace Haley.Abstractions {
    internal interface IWorkFlowAckRef {
        long AckId { get; }
        string AckGuid { get; }
    }
}
