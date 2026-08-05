using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace VirtualLeadersGuide.E2E.Tests;

// Exercises all three states of /dashboard's own authorization gate (P2.1-2, #60) - anonymous, signed in with
// no role, and signed in as an allowlisted Admin (P2-4, #13) - as opposed to LoginPageShould, which is scoped
// to the Login form's own behavior (ADR-0012). See LoginPageShould's header comment for the split rationale.
[Collection(nameof(AspireE2ECollection))]
public class DashboardAuthorizationShould : PageTest
{
    private readonly AspireE2EFixture _fixture;

    public DashboardAuthorizationShould(AspireE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RedirectToLoginWithReturnUrl_WhenAnonymous_ForDashboard()
    {
        await Page.GotoAsync(new Uri(_fixture.WebBaseUrl, "dashboard").ToString());

        // RedirectToLogin.razor builds `Account/Login?returnUrl=<the full dashboard URL, escaped>` - matching
        // on the path plus the lowercase `returnUrl=` key (not the full encoded value) keeps this from
        // breaking if the escaping scheme ever changes.
        await Expect(Page).ToHaveURLAsync(new Regex(@"Account/Login\?returnUrl=.*dashboard", RegexOptions.IgnoreCase));
    }

    [Fact]
    public async Task RedirectToNoAccess_WhenTheSignedInUserHoldsNoRoleClaim_ForDashboard()
    {
        string email = $"e2e-no-role-{Guid.NewGuid():n}@example.test";
        await _fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(_fixture.WebBaseUrl, email, TestCredentials.KnownPassword);

        await Page.GotoAsync(new Uri(_fixture.WebBaseUrl, "dashboard").ToString());

        await Expect(Page).ToHaveURLAsync(new Uri(_fixture.WebBaseUrl, "Account/NoAccess").ToString());
    }

    [Fact]
    public async Task RenderTheDashboard_WhenTheSignedInUserIsAnAllowlistedAdmin_ForDashboard()
    {
        // AdminAllowlistedEmail is baked into the AppHost's admin-allowlist parameter for this whole run (see
        // AspireE2EFixture) - creating the account here is enough; AdminAllowlistSynchronizer promotes it to
        // Admin during this same sign-in (ApplicationUserClaimsPrincipalFactory awaits the sync before the
        // cookie is written), so no separate grant-creation step is needed.
        await _fixture.IdentityApi.CreateUserAsync(
            _fixture.AdminAllowlistedEmail, TestCredentials.KnownPassword, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(
            _fixture.WebBaseUrl, _fixture.AdminAllowlistedEmail, TestCredentials.KnownPassword);

        await Page.GotoAsync(new Uri(_fixture.WebBaseUrl, "dashboard").ToString());

        await Expect(Page).ToHaveURLAsync(new Uri(_fixture.WebBaseUrl, "dashboard").ToString());
        await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Dashboard", Exact = true }))
            .ToBeVisibleAsync();
    }
}
