using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Exercises the forgot-password -> reset-link -> sign-in-with-new-password round trip end to end, using
/// <see cref="AspireE2EFixture.EmailSink"/> to intercept the reset email that would otherwise go out via
/// Azure Communication Services (P2.1-4, #62; ADR-0032). Scoped to the reset flow as a whole - spanning
/// <c>ForgotPassword.razor</c> and <c>ResetPassword.razor</c> - rather than either page alone, the same
/// concern-scoped naming <see cref="LoginPageScenarios"/>'s remarks explain for this project (ADR-0029).
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class PasswordResetScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given a confirmed user, when they complete the forgot-password flow, then they can sign in with the new password but not the old one")]
    public async Task GivenAConfirmedUser_WhenTheyCompleteTheForgotPasswordFlow_ThenTheyCanSignInWithTheNewPasswordButNotTheOldOne() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-password-reset-{Guid.NewGuid():n}@example.test";
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

            await SubmitForgotPasswordAsync(email);
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/ForgotPasswordConfirmation").ToString());

            SentEmailDto resetEmail = await Fixture.EmailSink.WaitForEmailAsync(email, CancellationToken.None);
            Assert.Equal(SentEmailKinds.PasswordResetLink, resetEmail.Kind);

            await Page.GotoAsync(resetEmail.Payload);
            await SubmitResetPasswordAsync(email, TestCredentials.RotatedPassword);
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/ResetPasswordConfirmation").ToString());

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.RotatedPassword);
            await Expect(Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }))
                .ToBeVisibleAsync();

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);
            await Expect(Page.GetByText("Error: Invalid login attempt.")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given an email with no account, when the forgot-password form is submitted, then it redirects the same way and writes no email")]
    public async Task GivenAnEmailWithNoAccount_WhenTheForgotPasswordFormIsSubmitted_ThenItRedirectsTheSameWayAndWritesNoEmail() =>
        await RunAsync(async () =>
        {
            string unknownEmail = $"e2e-password-reset-unknown-{Guid.NewGuid():n}@example.test";

            await SubmitForgotPasswordAsync(unknownEmail);
            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/ForgotPasswordConfirmation").ToString());

            await EstablishNoEmailWasWrittenHappensBeforeAsync();

            Assert.False(Fixture.EmailSink.HasEmailFor(unknownEmail));
        });

    /// <remarks>
    /// The negative half of AC #5 (no user-existence leak) needs a happens-before, not a sleep: Web returns
    /// the unknown-email submit's redirect only once it has fully processed that request, so if it were ever
    /// going to write a file for that email it already would have by then. Submitting for a real, freshly
    /// seeded user here and waiting for *their* email to land is that happens-before - once it lands, the
    /// earlier request's absence is proven, not merely unobserved at some earlier moment. This call looks like
    /// redundant setup; it is not - do not delete it.
    /// </remarks>
    private async Task EstablishNoEmailWasWrittenHappensBeforeAsync()
    {
        string knownEmail = $"e2e-password-reset-barrier-{Guid.NewGuid():n}@example.test";
        await Fixture.IdentityApi.CreateUserAsync(knownEmail, TestCredentials.KnownPassword, CancellationToken.None);
        await SubmitForgotPasswordAsync(knownEmail);
        await Fixture.EmailSink.WaitForEmailAsync(knownEmail, CancellationToken.None);
    }

    private async Task SubmitForgotPasswordAsync(string email)
    {
        await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "Account/ForgotPassword").ToString());
        await Page.Locator("#Input\\.Email").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Reset password" }).ClickAsync();
    }

    /// <remarks>
    /// <c>ResetPassword.razor</c> looks the account up by <see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}.FindByEmailAsync"/>
    /// before validating the code against it, so <paramref name="email"/> has to be filled even though the
    /// reset link itself carries no email - only the code.
    /// </remarks>
    private async Task SubmitResetPasswordAsync(string email, string newPassword)
    {
        await Page.Locator("#Input\\.Email").FillAsync(email);
        await Page.Locator("#Input\\.Password").FillAsync(newPassword);
        await Page.Locator("#Input\\.ConfirmPassword").FillAsync(newPassword);
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Reset" }).ClickAsync();
    }
}
