using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// The smoke test proving the whole arrangement works (P2.1-1, #59) - see <see cref="E2ETestBase"/>'s
/// remarks for why <see cref="AspireE2ECollection"/>'s constructor injection and
/// <c>Microsoft.Playwright.Xunit</c>'s own <see cref="IAsyncLifetime"/> chain don't conflict. Scoped to the
/// Login form's own behavior (P2.1-2, #60) - what happens after <c>/dashboard</c> is reached, and what a
/// signed-in session can do from there, live in <see cref="DashboardAuthorizationScenarios"/>/
/// <see cref="NavMenuScenarios"/> instead (ADR-0029 narrows ADR-0012 for this project: a test class should
/// be named for what it actually exercises).
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class LoginPageScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given the Login page, when navigating to Account/Login, then the Log in heading is visible")]
    public async Task GivenTheLoginPage_WhenNavigatingToAccountLogin_ThenTheLogInHeadingIsVisible() =>
        await RunAsync(async () =>
        {
            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "Account/Login").ToString());

            await Expect(GetLoginHeading()).ToBeVisibleAsync();
        });

    /// <remarks>
    /// <c>Exact = true</c>: without it, "Log in" substring-matches <c>Login.razor</c>'s
    /// <c>&lt;h2&gt;Use a local account to log in.&lt;/h2&gt;</c> too, and the locator becomes ambiguous
    /// (Playwright's strict mode rejects a 2-element match).
    /// </remarks>
    private ILocator GetLoginHeading() =>
        Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Log in", Exact = true });

    [Fact(DisplayName = "Given valid credentials, when submitting the Login form with a returnUrl, then the browser redirects to that returnUrl")]
    public async Task GivenValidCredentials_WhenSubmittingTheLoginFormWithAReturnUrl_ThenItRedirectsToThatReturnUrl() =>
        await RunAsync(async () =>
        {
            IdentityUserDto user = await CreateTrackedUserAsync("e2e-valid-login", CancellationToken.None);

            await new LoginPage(Page).SignInAsync(
                Fixture.WebBaseUrl, user.Email!, TestCredentials.KnownPassword, returnUrl: AuthorizeOnlyNoRoleCheckPath);

            await Expect(Page).ToHaveURLAsync(
                new Uri(Fixture.WebBaseUrl, AuthorizeOnlyNoRoleCheckPath.TrimStart('/')).ToString());
        });

    /// <remarks>
    /// [Authorize]-only, no role check - landing here after sign-in proves <c>ReturnUrl</c> was honored on
    /// its own. A <c>/dashboard</c> target would immediately redirect again (every fresh account holds no
    /// role yet), conflating this assertion with <see cref="DashboardAuthorizationScenarios"/>'s own
    /// no-role coverage.
    /// </remarks>
    private const string AuthorizeOnlyNoRoleCheckPath = "/Account/Manage";

    [Fact(DisplayName = "Given a wrong password, when submitting the Login form, then an invalid login error is shown")]
    public async Task GivenAWrongPassword_WhenSubmittingTheLoginForm_ThenAnInvalidLoginErrorIsShown() =>
        await RunAsync(async () =>
        {
            IdentityUserDto user = await CreateTrackedUserAsync("e2e-wrong-password", CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, user.Email!, "wrong-password");

            await Expect(Page.GetByText("Error: Invalid login attempt.")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a locked-out account, when submitting the Login form, then the browser redirects to Account/Lockout")]
    public async Task GivenALockedOutAccount_WhenSubmittingTheLoginForm_ThenItRedirectsToAccountLockout() =>
        await RunAsync(async () =>
        {
            IdentityUserDto user = await CreateTrackedUserAsync("e2e-locked-out", CancellationToken.None);
            await Fixture.IdentityApi.LockOutAsync(
                user, DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, user.Email!, TestCredentials.KnownPassword);

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/Lockout").ToString());
        });
}
