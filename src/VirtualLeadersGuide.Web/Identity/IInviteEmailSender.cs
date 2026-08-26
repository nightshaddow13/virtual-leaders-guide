namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Sends a Director invite email (P2-12, #43) - separate from <see cref="Microsoft.AspNetCore.Identity.IEmailSender{TUser}"/>
/// because that interface's three methods (confirmation link, password-reset link, password-reset code) have
/// no "you've been invited" shape, and their subject/body are chosen inside each sender rather than passed
/// in, so reusing <c>SendPasswordResetLinkAsync</c> would deliver "Reset your password" copy to someone who
/// has never had a password.
/// </summary>
/// <remarks>
/// Implemented alongside <see cref="Microsoft.AspNetCore.Identity.IEmailSender{TUser}"/> by both
/// <see cref="AcsEmailSender"/> and <see cref="FileSinkEmailSender"/>, and registered by
/// <see cref="EmailSenderRegistration"/> in the same fail-closed config fork (ADR-0032).
/// </remarks>
public interface IInviteEmailSender
{
    /// <summary>Sends an email inviting <paramref name="email"/> to set up their Director account.</summary>
    /// <param name="user">The just-created, passwordless <see cref="ApplicationUser"/> being invited.</param>
    /// <param name="email">The address to send to - <paramref name="user"/>'s own email.</param>
    /// <param name="setupLink">
    /// The absolute <c>/setup?t=...</c> URL carrying the invite token (<c>InviteTokenProvider</c>).
    /// </param>
    Task SendDirectorInviteAsync(ApplicationUser user, string email, string setupLink);
}
