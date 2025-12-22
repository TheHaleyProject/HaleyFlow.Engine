using System;
using System.Collections.Generic;
using System.Text;

namespace Haley.Enums {
    [Flags]
    public enum LifeCycleStateFlag : int {
        None =0,
        IsInitial = 1 << 0, //1
        IsFinal = 1 << 1, //2
        IsSystem = 1 << 2, //4
        IsError = 1 << 3 //8
    }
}