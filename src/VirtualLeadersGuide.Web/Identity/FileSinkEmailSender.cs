using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// <see cref="IEmailSender{TUser}"/> that writes each email to a JSON file instead of sending it, so a test
/// process can intercept it without a DI seam reachable across Web's process boundary (P2.1-4, #62; ADR-0032).
/// Selected via <see cref="EmailSenderRegistration"/>, never directly.
/// </summary>
/// <remarks>
/// Unlike <see cref="AcsEmailSender"/>, every method here writes a file rather than throwing on the two
/// <see cref="AcsEmailSender"/> treats as unsupported - see ADR-0032's Consequences for why the two senders
/// are not behaviourally equivalent. Writes <see cref="SentEmailDto.Payload"/> as received, per that
/// property's own "verbatim" contract - a test process navigating a captured link needs the real value, not
/// ciphertext only Web's own key ring (unreachable across the process boundary this sender exists to route
/// around) could undo.
/// </remarks>
public sealed class FileSinkEmailSender(IConfiguration configuration, ILogger<FileSinkEmailSender> logger)
    : IEmailSender<ApplicationUser>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static string ComputeKindFingerprint(string value)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash[..8]);
    }

    /// <inheritdoc/>
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        WriteAsync(email, "Confirm your email", SentEmailKinds.ConfirmationLink, confirmationLink);

    /// <inheritdoc/>
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        WriteAsync(email, "Reset your password", SentEmailKinds.PasswordResetLink, resetLink);

    /// <inheritdoc/>
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        WriteAsync(email, "Your password reset code", SentEmailKinds.PasswordResetCode, resetCode);

    /// <remarks>
    /// Writes to a <c>.tmp</c> file first, then <see cref="File.Move(string, string)"/>s it into place, so a
    /// poller watching the directory (<c>EmailFileSink</c> in <c>E2E.Tests</c>) never observes a partially
    /// written file. The temp name deliberately does not contain <c>.json</c> anywhere - a
    /// <c>*.json</c>-style glob on Windows also matches longer extensions, so <c>x.json.tmp</c> would still be
    /// visible mid-write.
    /// </remarks>
    private async Task WriteAsync(string toEmail, string subject, string kind, string payload)
    {
        var directory = configuration["Email:FileSinkDirectory"]
            ?? throw new InvalidOperationException("Email:FileSinkDirectory is not configured.");
        Directory.CreateDirectory(directory);

        var email = new SentEmailDto(toEmail, subject, kind, payload, DateTimeOffset.UtcNow);

        var fileName = Guid.NewGuid().ToString("n");
        var tempPath = Path.Combine(directory, fileName + ".tmp");
        var finalPath = Path.Combine(directory, fileName + ".json");

        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(email, SerializerOptions));
        File.Move(tempPath, finalPath);

        logger.LogInformation("Wrote email of kind fingerprint {KindFingerprint} to file {FileName}.", ComputeKindFingerprint(kind), fileName + ".json");
    }
}
