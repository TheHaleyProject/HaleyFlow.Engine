using Haley.Enums;
using System;

namespace Haley.Models {
    public class LifeCycleCategory {
        public int Id { get; set; }
        public string DisplayName { get; set; } = default!;
        public string Name { get; set; } = default!;
    }
}
