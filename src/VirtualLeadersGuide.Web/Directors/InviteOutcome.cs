namespace VirtualLeadersGuide.Web.Directors;

/// <summary>Outcomes <see cref="DirectorInviteService.InviteAsync"/> distinguishes.</summary>
public enum InviteOutcome
{
    Invited,

    /// <remarks>The email already belongs to a User (AC 4) - the caller should route to <see cref="InviteLookup.ExistingUser"/> instead.</remarks>
    AlreadyOnPlatform,

    /// <remarks>The identity store or Api's Grant/email seam was unreachable partway through.</remarks>
    StoreUnavailable
}
