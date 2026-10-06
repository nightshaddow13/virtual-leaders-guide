namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>Outcomes <see cref="ApiFacilityTypeClient.ListAsync"/> distinguishes.</summary>
public enum FacilityTypeReadOutcome
{
    Success,

    /// <remarks>
    /// The caller isn't an Admin - every verb on <c>/api/facilityTypes</c> is Admin-only (ADR-0071). In
    /// practice unreachable from <c>FacilityEditor</c>, whose own Admin gate already denies a non-Admin the
    /// page before this is ever called; kept distinct from <see cref="FacilityDataUnavailableException"/>
    /// rather than folded into it, matching every other Api client's discipline for an expected non-2xx.
    /// </remarks>
    Forbidden
}
