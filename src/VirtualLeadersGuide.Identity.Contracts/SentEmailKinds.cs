namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// Well-known <see cref="SentEmailDto.Kind"/> values, one per <c>IEmailSender{TUser}</c> method.
/// </summary>
/// <remarks>
/// Const strings rather than an enum, matching <see cref="RoleNames"/> - avoids configuring the JSON
/// serializer for enum-as-string on both the writing (Web) and reading (<c>E2E.Tests</c>) side.
/// </remarks>
public static class SentEmailKinds
{
    public const string PasswordResetLink = "PasswordResetLink";

    public const string ConfirmationLink = "ConfirmationLink";

    public const string PasswordResetCode = "PasswordResetCode";
}
