namespace VirtualLeadersGuide.Web.Activities;

/// <summary>Outcomes <see cref="ApiPlacementClient.CreateAsync"/> distinguishes.</summary>
public enum PlacementWriteOutcome
{
    Success,

    /// <remarks>The caller isn't an Admin or an assigned Director (ADR-0069's posture, extended to Placement).</remarks>
    Forbidden,

    /// <remarks>Api's <c>ActivityPlacementResourceDefinition</c> rejected the path with 422 - a missing Tab, or a Sub Section with no Section. The builder's own gating normally prevents both.</remarks>
    Invalid,

    /// <remarks>The same Activity is already placed at this exact path (ADR-0046) - normally caught earlier by the builder's own duplicate check, so this is the race/stale-tree case.</remarks>
    Conflict
}

/// <summary>Outcomes <see cref="ApiPlacementClient.DeleteAsync"/> distinguishes (P5-12, #97).</summary>
public enum PlacementRemoveOutcome
{
    Success,

    /// <remarks>The caller isn't an Admin or an assigned Director (ADR-0069's posture, extended to Placement).</remarks>
    Forbidden,

    /// <remarks>The Placement is already gone - another tab or user removed it first. The caller reloads the tree.</remarks>
    NotFound,

    /// <remarks>Api refused: this is the Activity's only Placement (ADR-0050). Normally caught client-side first, so this is the stale-tree case.</remarks>
    LastPlacement
}
