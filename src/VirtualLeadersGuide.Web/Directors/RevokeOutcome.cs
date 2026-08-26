namespace VirtualLeadersGuide.Web.Directors;

/// <summary>Outcomes <see cref="DirectorInviteService.RevokeAsync"/> distinguishes.</summary>
public enum RevokeOutcome
{
    /// <remarks>Full teardown per ADR-0035 - the <c>ApplicationUser</c> row is deleted, and the database's own cascade removes its <c>UserRole</c> rows.</remarks>
    Revoked,

    /// <remarks>The User already has a password - only a pending, un-activated Invite can be revoked.</remarks>
    AlreadyActive,

    NotFound
}
