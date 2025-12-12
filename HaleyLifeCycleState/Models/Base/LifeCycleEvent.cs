using System;

namespace Haley.Models {
    public class LifeCycleEvent {
        public int Id { get; set; }
        public int Code { get; set; }
        public string DisplayName { get; set; } = default!;
        public string Name { get; set; } = default!;
        public int DefinitionVersion { get; set; }
    }
}
