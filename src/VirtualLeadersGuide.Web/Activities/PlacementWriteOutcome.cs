namespace VirtualLeadersGuide.Web.Activities;

/// <summary>Outcomes <see cref="ApiPlacementClient.CreateAsync"/> and <see cref="ApiPlacementClient.MoveAsync"/> distinguish.</summary>
public enum PlacementWriteOutcome
{
    Success,

    /// <remarks>The caller isn't an Admin or an assigned Director (ADR-0069's posture, extended to Placement).</remarks>
    Forbidden,

    /// <remarks>On create, Api rejected the path with 422 - a missing Tab, or a Sub Section with no Section; the builder's own gating normally prevents both. On a move, the Placement no longer exists (404).</remarks>
    Invalid,

    /// <remarks>The same Activity is already placed at this exact path (ADR-0046) - normally caught earlier by the builder's own duplicate check, so this is the race/stale-tree case.</remarks>
    Conflict,

    /// <remarks>An InfoPage fills that screen, so no Activity can go there (ADR-0047; P5-24, #208).</remarks>
    SlotHasInfoPage
}

/// <summary>Outcomes <see cref="ApiPlacementClient.DeleteAsync"/> distinguishes (P5-12, #97).</summary>
public enum PlacementRemoveOutcome
{
    /// <remarks>Api deleted the Placement, and any Tier it alone kept alive.</remarks>
    Success,

    /// <remarks>The caller isn't an Admin or an assigned Director (ADR-0069's posture, extended to Placement).</remarks>
    Forbidden,

    /// <remarks>The Placement is already gone - another tab or user removed it first. The caller reloads the tree.</remarks>
    NotFound,

    /// <remarks>Api refused: this is the Activity's only Placement (ADR-0050). Normally caught client-side first, so this is the stale-tree case.</remarks>
    LastPlacement
}
