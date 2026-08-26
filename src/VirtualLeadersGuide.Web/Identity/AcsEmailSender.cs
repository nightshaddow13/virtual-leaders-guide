using Azure;
using Azure.Communication.Email;
using Microsoft.AspNetCore.Identity;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// <see cref="IEmailSender{TUser}"/> over Azure Communication Services Email (P2-1, #1) - reads
/// <c>Email:ConnectionString</c> and <c>Email:SenderAddress</c>, both wired by P2-1's
/// <c>AppHost.cs</c>/<c>appsettings.json</c> (<c>acs-connection-string</c> is a required, fail-closed
/// Aspire parameter, so there's no "connection string absent" state to fall back from here).
/// </summary>
/// <remarks>
/// Of the three <see cref="IEmailSender{TUser}"/> methods, only <see cref="SendPasswordResetLinkAsync"/> is
/// meaningfully implemented: <c>RequireConfirmedAccount</c> is false for P2-2, so nothing calls
/// <see cref="SendConfirmationLinkAsync"/>, and the Account pages carried over from the scaffold use the
/// link-based reset flow, not the code-based one, so nothing calls <see cref="SendPasswordResetCodeAsync"/>
/// either. Both throw rather than silently no-op or send a mismatched email - same "don't pretend to
/// support a capability with no caller" reasoning as omitting <c>IUserTwoFactorStore</c> from
/// <c>ApiUserStore</c> (ADR-0022). <see cref="IInviteEmailSender.SendDirectorInviteAsync"/> (P2-12, #43) is
/// a distinct interface, not a fourth <see cref="IEmailSender{TUser}"/> method, and is fully implemented.
/// </remarks>
public sealed class AcsEmailSender(IConfiguration configuration, ILogger<AcsEmailSender> logger)
    : IEmailSender<ApplicationUser>, IInviteEmailSender
{
    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        await SendAsync(email, "Reset your password",
            $"<p>Please reset your password by <a href='{resetLink}'>clicking here</a>.</p>", "password reset");

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        throw new NotSupportedException(
            "Email confirmation is not used in this app (RequireConfirmedAccount is false) - nothing should call this.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        throw new NotSupportedException(
            "Code-based password reset is not used in this app - the Account pages use the link-based flow.");

    /// <inheritdoc/>
    public async Task SendDirectorInviteAsync(ApplicationUser user, string email, string setupLink) =>
        await SendAsync(email, "You've been invited as a Director",
            $"<p>You've been invited to Virtual Leaders Guide as a Director. "
            + $"<a href='{setupLink}'>Set your password</a> to activate your account.</p>",
            "Director invite");

    private async Task SendAsync(string email, string subject, string html, string logDescription)
    {
        var connectionString = configuration["Email:ConnectionString"]
            ?? throw new InvalidOperationException("Email:ConnectionString is not configured.");
        var senderAddress = configuration["Email:SenderAddress"]
            ?? throw new InvalidOperationException("Email:SenderAddress is not configured.");

        var client = new EmailClient(connectionString);
        var content = new EmailContent(subject) { Html = html };
        var message = new EmailMessage(senderAddress, email, content);

        EmailSendOperation operation = await client.SendAsync(WaitUntil.Completed, message);
        logger.LogInformation(
            "Sent {LogDescription} email, operation id {OperationId}, status {Status}.",
            logDescription, operation.Id, operation.Value.Status);
    }
}
