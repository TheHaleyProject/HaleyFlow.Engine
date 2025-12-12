using Haley.Enums;
using System;

namespace Haley.Models {
    public class LifeCycleState {
        public int Id { get; set; }
        public string DisplayName { get; set; } = default!;
        public string Name { get; set; } = default!;
        public LifeCycleStateFlag Flags { get; set; }
        public int Category { get; set; }
        public int DefinitionVersion { get; set; }
        public DateTime Created { get; set; }
    }
}
