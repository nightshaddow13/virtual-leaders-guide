namespace VirtualLeadersGuide.Web.Directors;

/// <summary>Outcomes <see cref="DirectorInviteService.ResendAsync"/> distinguishes.</summary>
public enum ResendOutcome
{
    Sent,

    /// <remarks>The User already has a password - resend only applies to a pending, un-activated Invite.</remarks>
    AlreadyActive,

    NotFound,

    /// <remarks>
    /// The security stamp was already rotated by the time this is returned (ADR-0020's resend still needs a
    /// stamp rotation to invalidate whatever link was sent last time), so a prior outstanding link no longer
    /// works even though this attempt didn't deliver a replacement - the caller should tell the Admin to
    /// resend again rather than treating this as a no-op.
    /// </remarks>
    SendFailed
}
