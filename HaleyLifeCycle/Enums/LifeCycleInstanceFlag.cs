using System;

namespace Haley.Enums {

    [Flags]
    public enum LifeCycleInstanceFlag : int {
        None = 0,
        Active = 1 << 0,
        Suspended = 1 << 1,
        Completed = 1 << 2,
        Failed = 1 << 3,
        Archived = 1 << 4
    }
}