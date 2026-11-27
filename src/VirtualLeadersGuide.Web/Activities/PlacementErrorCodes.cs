namespace VirtualLeadersGuide.Web.Activities;

/// <summary>
/// The <c>code</c> values Api puts on a Placement 409 (ADR-0047's amendment) - mirrors Api's
/// <c>PlacementErrors</c>, which can't be shared across the project boundary.
/// </summary>
internal static class PlacementErrorCodes
{
    /// <summary>An Activity was placed on a screen an InfoPage fills.</summary>
    public const string SlotHasInfoPage = "slot-has-infopage";

    /// <summary>An InfoPage was placed on a screen that shows Activities.</summary>
    public const string SlotHasActivities = "slot-has-activities";

    /// <summary>An InfoPage was placed on a screen another InfoPage fills.</summary>
    public const string SlotHasOtherInfoPage = "slot-has-other-infopage";

    /// <summary>The InfoPage already has its one Placement.</summary>
    public const string InfoPageAlreadyPlaced = "infopage-already-placed";
}
