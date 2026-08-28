using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Validates that a password is between 6 and 100 characters long.
/// </summary>
/// <remarks>
/// Replaces an identical <see cref="StringLengthAttribute"/> stack that was previously repeated verbatim on
/// three <c>InputModel</c>s (<c>SetupAccount</c>, <c>ResetPassword</c>, <c>Manage/ChangePassword</c>) - see
/// ADR-0040. Deliberately doesn't also enforce non-empty: pair this with <see cref="RequiredAttribute"/> on
/// the same property, matching how every other required field in the app is validated.
/// </remarks>
public sealed class PasswordLengthAttribute : ValidationAttribute
{
    private const int MinLength = 6;
    private const int MaxLength = 100;

    public PasswordLengthAttribute()
        : base("The {0} must be at least {2} and at max {1} characters long.")
    {
    }

    public override bool IsValid(object? value) =>
        value is not string password || (password.Length >= MinLength && password.Length <= MaxLength);

    public override string FormatErrorMessage(string name) =>
        string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, MaxLength, MinLength);
}
