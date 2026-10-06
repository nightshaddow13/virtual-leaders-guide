namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>Outcomes <see cref="ApiFacilityTypeClient.CreateAsync"/> distinguishes.</summary>
public enum FacilityTypeWriteOutcome
{
    Success,

    /// <remarks>
    /// The caller isn't an Admin - every verb on <c>/api/facilityTypes</c> is Admin-only (ADR-0071),
    /// Read included, so this also covers the claim-lag case where a since-demoted Admin's cookie still
    /// says otherwise.
    /// </remarks>
    Forbidden,

    /// <remarks>
    /// Api's <c>FacilityTypeResourceDefinition</c> rejected the write with 409 - another caller created a
    /// same-named Facility Type between <see cref="ApiFacilityTypeClient.ListAsync"/>'s snapshot and this
    /// call. <c>FacilityEditor</c> treats this the same as "use the existing one" by re-listing rather than
    /// surfacing it as a hard failure - two Admins typing the same brand-new type at once is a benign race,
    /// not a real conflict from the caller's point of view.
    /// </remarks>
    Conflict
}
