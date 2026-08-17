using Microsoft.Playwright;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

// The smoke test proving the whole arrangement works (P2.1-1, #59): AspireE2ECollection's constructor
// injection and Microsoft.Playwright.Xunit's own IAsyncLifetime chain (see PageTest -> ContextTest ->
// BrowserTest -> PlaywrightTest -> WorkerAwareTest -> ExceptionCapturer) don't conflict - xUnit resolves the
// fixture purely by matching this constructor's parameter type, independently of the base class's own
// IAsyncLifetime hooks, which still run afterward exactly as they would with no fixture involved.
//
// Scoped to the Login form's own behavior (P2.1-2, #60) - what happens after /dashboard is reached, and
// what a signed-in session can do from there, live in DashboardAuthorizationScenarios/NavMenuScenarios
// instead (ADR-0012, narrowed by ADR-0027 for this project: a test class should be named for what it
// actually exercises).
[Collection(nameof(AspireE2ECollection))]
public class LoginPageScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given the Login page, when navigating to Account/Login, then the Log in heading is visible")]
    public async Task GivenTheLoginPage_WhenNavigatingToAccountLogin_ThenTheLogInHeadingIsVisible() =>
        await RunAsync(async () =>
        {
            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "Account/Login").ToString());

            // Exact = true: without it, "Log in" substring-matches Login.razor's <h2>Use a local account to
            // log in.</h2> too, and the locator becomes ambiguous (Playwright's strict mode rejects a
            // 2-element match).
            await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Log in", Exact = true }))
                .ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given valid credentials, when submitting the Login form with a returnUrl, then the browser redirects to that returnUrl")]
    public async Task GivenValidCredentials_WhenSubmittingTheLoginFormWithAReturnUrl_ThenItRedirectsToThatReturnUrl() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-valid-login-{Guid.NewGuid():n}@example.test";
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

            // Account/Manage rather than /dashboard: it's [Authorize]-only (no role check), so landing there
            // proves ReturnUrl was honored on its own - a /dashboard target would immediately redirect again
            // (every fresh account holds no role yet), conflating this assertion with
            // DashboardAuthorizationScenarios's own no-role coverage.
            await new LoginPage(Page).SignInAsync(
                Fixture.WebBaseUrl, email, TestCredentials.KnownPassword, returnUrl: "/Account/Manage");

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/Manage").ToString());
        });

    [Fact(DisplayName = "Given a wrong password, when submitting the Login form, then an invalid login error is shown")]
    public async Task GivenAWrongPassword_WhenSubmittingTheLoginForm_ThenAnInvalidLoginErrorIsShown() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-wrong-password-{Guid.NewGuid():n}@example.test";
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, "wrong-password");

            await Expect(Page.GetByText("Error: Invalid login attempt.")).ToBeVisibleAsync();
        });

    [Fact(DisplayName = "Given a locked-out account, when submitting the Login form, then the browser redirects to Account/Lockout")]
    public async Task GivenALockedOutAccount_WhenSubmittingTheLoginForm_ThenItRedirectsToAccountLockout() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-locked-out-{Guid.NewGuid():n}@example.test";
            IdentityUserDto user = await Fixture.IdentityApi.CreateUserAsync(
                email, TestCredentials.KnownPassword, CancellationToken.None);
            await Fixture.IdentityApi.LockOutAsync(
                user, DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/Lockout").ToString());
        });
}
