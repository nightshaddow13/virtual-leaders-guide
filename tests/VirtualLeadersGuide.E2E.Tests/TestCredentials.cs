namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>Shared test-only values used across this project's <c>[Fact]</c> methods.</summary>
internal static class TestCredentials
{
    /// <summary>
    /// A password satisfying ASP.NET Core Identity's unrelaxed default policy (at least 6 characters, a
    /// digit, an uppercase letter, a lowercase letter, and a non-alphanumeric character) - used by every test
    /// that seeds an account via <see cref="IdentityApiClient.CreateUserAsync"/>. The value itself carries no
    /// meaning; no test asserts on it, only that submitting it correctly (or incorrectly) succeeds or fails.
    /// Not randomized, unlike test emails - nothing here needs per-test uniqueness the way account creation
    /// does.
    /// </summary>
    public const string KnownPassword = "P@ssw0rd123!";

    /// <summary>
    /// A second password satisfying the same policy as <see cref="KnownPassword"/>, submitted to
    /// <c>Account/ResetPassword</c> by <c>PasswordResetScenarios</c> - distinct from
    /// <see cref="KnownPassword"/> only so a test can assert the old password stops working after a reset.
    /// </summary>
    public const string RotatedPassword = "N3wP@ssw0rd!";
}
