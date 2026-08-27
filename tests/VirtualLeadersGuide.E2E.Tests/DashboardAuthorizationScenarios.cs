using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Exercises all three states of <c>/dashboard</c>'s own authorization gate (P2.1-2, #60) - anonymous,
/// signed in with no role, and signed in as an allowlisted Admin (P2-4, #13) - as opposed to
/// <see cref="LoginPageScenarios"/>, which is scoped to the Login form's own behavior (ADR-0029 narrows
/// ADR-0012). See <see cref="LoginPageScenarios"/>'s remarks for the split rationale. The no-role case signs
/// in as the fixture <see cref="AspireE2EFixture.NoRoleEmail"/> account rather than a throwaway one - nothing
/// here mutates account state, so there's nothing to isolate (ADR-0039).
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class DashboardAuthorizationScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given an anonymous user, when navigating to /dashboard, then the browser redirects to Account/Login with a returnUrl")]
    public async Task GivenAnAnonymousUser_WhenNavigatingToDashboard_ThenItRedirectsToLoginWithAReturnUrl() =>
        await RunAsync(async () =>
        {
            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());

            await Expect(Page).ToHaveURLAsync(RedirectToLoginWithReturnUrl());
        });

    /// <remarks>
    /// <c>RedirectToLogin.razor</c> builds <c>Account/Login?returnUrl=&lt;the full dashboard URL,
    /// escaped&gt;</c> - matching on the path plus the lowercase <c>returnUrl=</c> key (not the full encoded
    /// value) keeps this from breaking if the escaping scheme ever changes.
    /// </remarks>
    private static Regex RedirectToLoginWithReturnUrl() =>
        new(@"Account/Login\?returnUrl=.*dashboard", RegexOptions.IgnoreCase);

    [Fact(DisplayName = "Given a signed-in user with no role claim, when navigating to /dashboard, then the browser redirects to Account/NoAccess")]
    public async Task GivenANoRoleUser_WhenNavigatingToDashboard_ThenItRedirectsToNoAccess() =>
        await RunAsync(async () =>
        {
            await new LoginPage(Page).SignInAsync(
                Fixture.WebBaseUrl, AspireE2EFixture.NoRoleEmail, TestCredentials.KnownPassword);

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "Account/NoAccess").ToString());
        });

    [Fact(DisplayName = "Given a signed-in allowlisted Admin, when navigating to /dashboard, then the Dashboard renders")]
    public async Task GivenAnAllowlistedAdmin_WhenNavigatingToDashboard_ThenTheDashboardRenders() =>
        await RunAsync(async () =>
        {
            await SignInAsAdminAsync();

            await Page.GotoAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());

            await Expect(Page).ToHaveURLAsync(new Uri(Fixture.WebBaseUrl, "dashboard").ToString());
            await Expect(Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Events", Exact = true }))
                .ToBeVisibleAsync();
        });
}
