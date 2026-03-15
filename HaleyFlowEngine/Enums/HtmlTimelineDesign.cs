namespace Haley.Enums;

public enum HtmlTimelineDesign {
    /// <summary>Light glass card-based timeline with vertical rail and compact toggle.</summary>
    LightGlass = 0,
    /// <summary>Horizontal progress rail + step cards with colored number strips and table-style activities.</summary>
    FlowSteps  = 1,
    /// <summary>Audit-log table with per-row expand/collapse detail panel and compact column toggle.</summary>
    AuditLog   = 2,
    /// <summary>Operational control-board layout with summary rail, state path sidebar, and rich transition cards.</summary>
    ControlBoard = 3
}
