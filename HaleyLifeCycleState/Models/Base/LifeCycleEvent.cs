using System;

namespace Haley.Models {
    public sealed class LifeCycleEvent {
        public int Id { get; set; }
        public int DefinitionVersion { get; set; } // def_version
        public int Code { get; set; }              // stable
        public string DisplayName { get; set; } = "";
        public string Name { get; set; } = "";     // lower(display_name) from generated column
        public DateTime Created { get; set; }
    }
}
