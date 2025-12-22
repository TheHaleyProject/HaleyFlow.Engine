using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class LifeCycleMonitorOptions {
        public int PollIntervalSeconds { get; set; } = 15;
        public int AckMaxRetry { get; set; } = 5;
        public int AckRetryAfterMinutes { get; set; } = 5;
        public int TimeoutBatchSize { get; set; } = 50;
        public int AckBatchSize { get; set; } = 50;
        public int ConsumerId { get; set; } = 1;
    }
}
