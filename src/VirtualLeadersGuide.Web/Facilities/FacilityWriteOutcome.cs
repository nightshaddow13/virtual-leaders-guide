namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>Outcomes <see cref="ApiFacilityClient.CreateAsync"/> distinguishes.</summary>
public enum FacilityWriteOutcome
{
    Success,

    /// <remarks>
    /// The caller isn't an Admin (ADR-0070) - covers both a Director's write attempt and the claim-lag case
    /// where a since-demoted Admin's cookie still says otherwise.
    /// </remarks>
    Forbidden,

    /// <remarks>
    /// Api's <c>FacilityResourceDefinition</c> rejected the write with 422 - in practice only reachable via
    /// the unknown-Facility-Type pointer (<c>/data/attributes/facilityTypeId</c>), which normal use of
    /// <c>FacilityEditor</c>'s autofill can't trigger since the id always comes from a just-resolved or
    /// just-created <see cref="FacilityTypeDto"/>.
    /// </remarks>
    Invalid
}
