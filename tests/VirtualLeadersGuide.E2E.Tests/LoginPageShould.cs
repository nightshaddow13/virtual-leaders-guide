using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

// The smoke test proving the whole arrangement works (P2.1-1, #59): AspireE2ECollection's constructor
// injection and Microsoft.Playwright.Xunit's own IAsyncLifetime chain (see PageTest -> ContextTest ->
// BrowserTest -> PlaywrightTest -> WorkerAwareTest -> ExceptionCapturer) don't conflict - xUnit resolves the
// fixture purely by matching this constructor's parameter type, independently of the base class's own
// IAsyncLifetime hooks, which still run afterward exactly as they would with no fixture involved.
//
// Scoped to the Login form's own behavior (P2.1-2, #60) - what happens after /dashboard is reached, and
// what a signed-in session can do from there, live in DashboardAuthorizationShould/NavMenuShould instead
// (ADR-0012: a test class should be named for what it actually exercises).
[Collection(nameof(AspireE2ECollection))]
public class LoginPageShould : PageTest
{
    private readonly AspireE2EFixture _fixture;

    public LoginPageShould(AspireE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RenderTheLoginHeading_WhenNavigatingToAccountLogin_ForLoginPage()
    {
        await Page.GotoAsync(new Uri(_fixture.WebBaseUrl, "Account/Login").ToString());

        // Exact = true: without it, "Log in" substring-matches Login.razor's <h2>Use a local account to log
        // in.</h2> too, and the locator becomes ambiguous (Playwright's strict mode rejects a 2-element match).
        await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Log in", Exact = true }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task RedirectToTheReturnUrl_WhenCredentialsAreValid_ForLoginSubmit()
    {
        string email = $"e2e-valid-login-{Guid.NewGuid():n}@example.test";
        await _fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

        // Account/Manage rather than /dashboard: it's [Authorize]-only (no role check), so landing there
        // proves ReturnUrl was honored on its own - a /dashboard target would immediately redirect again
        // (every fresh account holds no role yet), conflating this assertion with
        // DashboardAuthorizationShould's own no-role coverage.
        await new LoginPage(Page).SignInAsync(
            _fixture.WebBaseUrl, email, TestCredentials.KnownPassword, returnUrl: "/Account/Manage");

        await Expect(Page).ToHaveURLAsync(new Uri(_fixture.WebBaseUrl, "Account/Manage").ToString());
    }

    [Fact]
    public async Task ShowInvalidLoginError_WhenPasswordIsWrong_ForLoginSubmit()
    {
        string email = $"e2e-wrong-password-{Guid.NewGuid():n}@example.test";
        await _fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(_fixture.WebBaseUrl, email, "wrong-password");

        await Expect(Page.GetByText("Error: Invalid login attempt.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RedirectToLockout_WhenAccountIsLockedOut_ForLoginSubmit()
    {
        string email = $"e2e-locked-out-{Guid.NewGuid():n}@example.test";
        IdentityUserDto user = await _fixture.IdentityApi.CreateUserAsync(
            email, TestCredentials.KnownPassword, CancellationToken.None);
        await _fixture.IdentityApi.LockOutAsync(
            user, DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

        await new LoginPage(Page).SignInAsync(_fixture.WebBaseUrl, email, TestCredentials.KnownPassword);

        await Expect(Page).ToHaveURLAsync(new Uri(_fixture.WebBaseUrl, "Account/Lockout").ToString());
    }
}
