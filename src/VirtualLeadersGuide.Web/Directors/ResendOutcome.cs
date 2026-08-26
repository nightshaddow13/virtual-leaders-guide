namespace VirtualLeadersGuide.Web.Directors;

/// <summary>Outcomes <see cref="DirectorInviteService.ResendAsync"/> distinguishes.</summary>
public enum ResendOutcome
{
    Sent,

    /// <remarks>The User already has a password - resend only applies to a pending, un-activated Invite.</remarks>
    AlreadyActive,

    NotFound
}
