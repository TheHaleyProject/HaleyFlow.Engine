using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Enums {
    public enum StateMachineNoticeKind {
        None = 0,
        DuplicateRequest = 1,          // same RequestId / idempotency key already processed
        NoOpAlreadyInState = 2,        // event valid, but results in no state change
        TransitionSuppressed = 3,      // policy/rule suppressed raising transition / side effects
        ReplayedFromStore = 4,         // you served prior result from DB/logs
        InvalidRequest = 5,            // missing fields, bad event code, etc. (not exception)
        Rejected = 6,                  
        RateLimited = 7,               
    }
}
