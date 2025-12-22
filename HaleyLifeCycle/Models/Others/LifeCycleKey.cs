using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    //public readonly record struct LifeCycleKey(LifeCycleKeyType Type, object A, object? B = null);
    public readonly record struct LifeCycleKey(LifeCycleKeyType Type, params object[] keys);
}
