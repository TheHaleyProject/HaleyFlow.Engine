
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class ResolvedState {
        public int StateId { get; set; }           // state.id
        public string Name { get; set; } = "";
        public int CategoryId { get; set; }        // category.id
        public int Flags { get; set; }             // LifeCycleStateFlag (int)
        public int? TimeoutSeconds { get; set; }   // NULL if no timeout
        public int TimeoutMode { get; set; }       // 0=once,1=repeat (match DB)
        public int? TimeoutEventId { get; set; }   // events.id (not code)
    }
}
