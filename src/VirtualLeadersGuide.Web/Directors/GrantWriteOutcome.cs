namespace VirtualLeadersGuide.Web.Directors;

/// <summary>Outcomes <see cref="ApiDirectorClient.GrantDirectorRoleAsync"/> and <see cref="ApiDirectorClient.GrantEventAccessAsync"/> distinguish.</summary>
public enum GrantWriteOutcome
{
    Created,

    /// <remarks>
    /// <c>/api/roleGrants</c>' duplicate-grant pre-check (<c>UserRoleResourceDefinition.CheckForConflictsAsync</c>)
    /// rejected this exact (user, role, scope) combination with 409 - the User already holds it.
    /// </remarks>
    AlreadyGranted,

    /// <remarks>
    /// The caller isn't an Admin (ADR-0033) - normally unreachable, since only the Users/EventEditor screens
    /// that already gate on Admin call this, but covers the claim-lag case where a since-demoted Admin's
    /// cookie still says otherwise (mirrors <c>EventWriteOutcome.Forbidden</c>).
    /// </remarks>
    Forbidden
}
