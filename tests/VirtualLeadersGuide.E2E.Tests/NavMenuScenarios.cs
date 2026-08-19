using Microsoft.Playwright;

namespace VirtualLeadersGuide.E2E.Tests;

/// <remarks>
/// Scoped to <c>NavMenu.razor</c>'s own sign-out form (P2.1-2, #60) - a single one-off assertion, not a page
/// object (see <see cref="LoginPage"/>'s remarks for why it's the only page object in this project).
/// </remarks>
[Collection(nameof(AspireE2ECollection))]
public class NavMenuScenarios(AspireE2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact(DisplayName = "Given a signed-in user, when the sign-out form is submitted, then the NavMenu shows a Sign in link")]
    public async Task GivenASignedInUser_WhenSignOutIsSubmitted_ThenTheNavMenuShowsSignIn() =>
        await RunAsync(async () =>
        {
            string email = $"e2e-sign-out-{Guid.NewGuid():n}@example.test";
            await Fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

            await new LoginPage(Page).SignInAsync(Fixture.WebBaseUrl, email, TestCredentials.KnownPassword);

            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();

            await Expect(Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Sign in" })).ToBeVisibleAsync();
        });
}
