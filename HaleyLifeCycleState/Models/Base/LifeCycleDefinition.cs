using System;

namespace Haley.Models {
    public class LifeCycleDefinition {
        public int Id { get; set; }
        public Guid Guid { get; set; }
        public string DisplayName { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public int EnvironmentId { get; set; }  
        public int EnvironmentCode { get; set; }  
        public DateTime Created { get; set; }
    }
}
