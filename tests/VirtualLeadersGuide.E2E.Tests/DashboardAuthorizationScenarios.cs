using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace VirtualLeadersGuide.E2E.Tests;

// Exercises all three states of /dashboard's own authorization gate (P2.1-2, #60) - anonymous, signed in with
// no role, and signed in as an allowlisted Admin (P2-4, #13) - as opposed to LoginPageScenarios, which is
// scoped to the Login form's own behavior (ADR-0012, narrowed by ADR-0027). See LoginPageScenarios's header
// comment for the split rationale.
[Collection(nameof(AspireE2ECollection))]
public class DashboardAuthorizationScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given an anonymous user, when navigating to /dashboard, then the browser redirects to Account/Login with a returnUrl")]
    public async Task GivenAnAnonymousUser_WhenNavigatingToDashboard_ThenItRedirectsToLoginWithAReturnUrl() =>
        await RunAsync(async () =>
        {
            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());

            // RedirectToLogin.razor builds `Account/Login?returnUrl=<the full dashboard URL, escaped>` -
            // matching on the path plus the lowercase `returnUrl=` key (not the full encoded value) keeps
            // this from breaking if the escaping scheme ever changes.
            await Expect(Page).ToHaveURLAsync(new Regex(@"Account/Login\?returnUrl=.*dashboard", RegexOptions.IgnoreCase));
        });

    [Fact(DisplayName = "Given a signed-in user with no role claim, when navigating to /dashboard, then the browser redirects to Account/NoAccess")]
    public async Task GivenANoRoleUser_WhenNavigatingToDashboard_ThenItRedirectsToNoAccess() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-no-role-{Guid.NewGuid():n}@example.test";
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/NoAccess").ToString());
        });

    [Fact(DisplayName = "Given a signed-in allowlisted Admin, when navigating to /dashboard, then the Dashboard renders")]
    public async Task GivenAnAllowlistedAdmin_WhenNavigatingToDashboard_ThenTheDashboardRenders() =>
        await RunAsync(async () =>
        {
            // AdminAllowlistedEmail is baked into the AppHost's admin-allowlist parameter for this whole run
            // (see AspireE2EFixture) - creating the account here is enough; AdminAllowlistSynchronizer
            // promotes it to Admin during this same sign-in (ApplicationUserClaimsPrincipalFactory awaits the
            // sync before the cookie is written), so no separate grant-creation step is needed.
            await Fixture.IdentityApi.CreateUserAsync(
                Fixture.AdminAllowlistedEmail, TestCredentials.KnownPassword, CancellationToken.None);

            await new LoginPage(Page).SignInAsync(
                Fixture.WebBaseUrl, Fixture.AdminAllowlistedEmail, TestCredentials.KnownPassword);

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Dashboard", Exact = true }))
                .ToBeVisibleAsync();
        });
}
