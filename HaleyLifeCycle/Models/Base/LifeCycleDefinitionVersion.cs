using System;

namespace Haley.Models {
    public class LifeCycleDefinitionVersion {
        public int Id { get; set; }
        public int Parent { get; set; }
        public Guid Guid { get; set; }
        public int Version { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public string Data { get; set; } = default!;
    }
}
