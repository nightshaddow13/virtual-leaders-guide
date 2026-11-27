namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>Outcomes <see cref="ApiInfoPagePlacementClient"/> distinguishes (P5-24, #208).</summary>
public enum InfoPagePlacementWriteOutcome
{
    /// <remarks>Api made the change.</remarks>
    Success,

    /// <remarks>The caller isn't an Admin or an assigned Director.</remarks>
    Forbidden,

    /// <remarks>The Placement being moved or removed is already gone.</remarks>
    NotFound,

    /// <remarks>Api rejected the path with 422.</remarks>
    Invalid,

    /// <remarks>That screen shows Activities (ADR-0047).</remarks>
    SlotHasActivities,

    /// <remarks>Another InfoPage fills that screen (ADR-0047).</remarks>
    SlotHasOtherInfoPage,

    /// <remarks>This InfoPage already has its one Placement - the page was stale.</remarks>
    AlreadyPlaced
}
