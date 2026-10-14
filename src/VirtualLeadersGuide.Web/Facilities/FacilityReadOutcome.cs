namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>Outcomes <see cref="ApiFacilityClient.ListAsync"/> and <see cref="ApiFacilityClient.GetAsync"/> distinguish.</summary>
public enum FacilityReadOutcome
{
    Success,

    /// <remarks>
    /// The caller holds no recognized role claim at all - <c>FacilityResourceDefinition</c> denies a
    /// collection read outright rather than narrowing it, since a Facility has no owning Event to narrow
    /// against (ADR-0070). Reachable from <c>Components.Pages.FacilityList</c> as the claims-lag safety net
    /// behind its own <c>EventAccessView.IsAdmin</c> gate (ADR-0031 - that gate is a rendering hint, never
    /// the authority).
    /// </remarks>
    Forbidden,

    /// <summary>No Facility has the requested id.</summary>
    /// <remarks>
    /// The Facility id doesn't resolve - a stale bookmark, or a row another Admin deleted between the list
    /// render and the click. Only <see cref="ApiFacilityClient.GetAsync"/> produces it; a collection read
    /// has no single id to miss.
    /// </remarks>
    NotFound
}
