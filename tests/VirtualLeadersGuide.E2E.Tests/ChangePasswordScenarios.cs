using Microsoft.Playwright;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Scoped to <c>ChangePassword.razor</c> alone (P2.1-5, #63) - the same concern-scoped naming
/// <see cref="LoginPageScenarios"/>'s remarks explain for this project (ADR-0029). Download/delete on
/// the Personal data page live in <see cref="PersonalDataScenarios"/> instead.
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class ChangePasswordScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given a signed-in user, when they change their password, then they can sign in with the new password but not the old one")]
    public async Task GivenASignedInUser_WhenTheyChangeTheirPassword_ThenTheyCanSignInWithTheNewPasswordButNotTheOldOne() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-change-password-{Guid.NewGuid():n}@example.test";
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);
            await SubmitChangePasswordAsync(TestCredentials.KnownPassword, TestCredentials.RotatedPassword);

            await Expect(Page.GetByText("Your password has been changed")).ToBeVisibleAsync();

            await SignOutAsync();

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.RotatedPassword);
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }))
                .ToBeVisibleAsync();

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);
            await Expect(Page.GetByText("Error: Invalid login attempt.")).ToBeVisibleAsync();
        });

    private async Task SubmitChangePasswordAsync(string oldPassword, string newPassword)
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "Account/Manage/ChangePassword").ToString());
        await Page.Locator("#Input\\.OldPassword").FillAsync(oldPassword);
        await Page.Locator("#Input\\.NewPassword").FillAsync(newPassword);
        await Page.Locator("#Input\\.ConfirmPassword").FillAsync(newPassword);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Update password" }).ClickAsync();
    }

    /// <remarks>
    /// Load-bearing, not cleanup: <c>ChangePassword.razor</c> calls <c>RefreshSignInAsync</c> on success, so
    /// the pre-change session survives the change. Without signing out here first, re-signing-in with the
    /// rotated password would find "Sign out" visible off that *pre-existing* cookie even if the change had
    /// silently failed - a false pass. <see cref="PasswordResetScenarios"/> doesn't need this because it
    /// starts signed out.
    /// </remarks>
    private async Task SignOutAsync() =>
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();
}
