namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// One email as captured by Web's file-sink <c>IEmailSender{TUser}</c> implementation, serialized to a JSON
/// file that a test process can poll for.
/// </summary>
/// <remarks>
/// Shared here rather than duplicated in Web and <c>E2E.Tests</c> - both already reference this assembly, so
/// a field rename becomes a compile error instead of a test-timeout mystery. See ADR-0032 for why the sink
/// exists and why it lives in <c>Identity.Contracts</c> rather than a project of its own.
/// </remarks>
/// <param name="To">The recipient's email address.</param>
/// <param name="Subject">The email's subject line.</param>
/// <param name="Kind">One of <see cref="SentEmailKinds"/>, naming which <c>IEmailSender{TUser}</c> method sent this.</param>
/// <param name="Payload">
/// The method's third argument verbatim - a link for <see cref="SentEmailKinds.PasswordResetLink"/>/
/// <see cref="SentEmailKinds.ConfirmationLink"/>, a short code for <see cref="SentEmailKinds.PasswordResetCode"/>.
/// Not named <c>Link</c>: it isn't one for every <see cref="Kind"/>.
/// </param>
/// <param name="SentAtUtc">When the sink wrote this file.</param>
public sealed record SentEmailDto(string To, string Subject, string Kind, string Payload, DateTimeOffset SentAtUtc);
