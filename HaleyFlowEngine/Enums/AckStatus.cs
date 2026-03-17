using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Enums {
    internal enum AckStatus {
        //This is for DB persistence only. If application sends (retry), then we shift the ackstatus to pending and then increase the retry count.
        Pending = 1,
        Delivered=2,
        Processed=3,
        Failed=4,
        /// <summary>
        /// Set by the engine (monitor timeout path) when a blocking hook ACK is forcibly closed
        /// before the instance transitions away. A consumer sending an ACK for a Cancelled row
        /// receives a STALE_ACK_RECEIVED notice and the update is rejected.
        /// </summary>
        Cancelled=5
    }
}
