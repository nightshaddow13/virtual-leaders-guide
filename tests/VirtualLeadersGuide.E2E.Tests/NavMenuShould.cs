using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace VirtualLeadersGuide.E2E.Tests;

// Scoped to NavMenu.razor's own sign-out form (P2.1-2, #60) - a single one-off assertion, not a page object
// (see LoginPageShould's header comment for why LoginPage is the only page object in this project).
[Collection(nameof(AspireE2ECollection))]
public class NavMenuShould : PageTest
{
    private readonly AspireE2EFixture _fixture;

    public NavMenuShould(AspireE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ShowSignInLink_WhenSignOutFormIsSubmitted_ForNavMenu()
    {
        string email = $"e2e-sign-out-{Guid.NewGuid():n}@example.test";
        await _fixture.IdentityApi.CreateUserAsync(email, TestCredentials.KnownPassword, CancellationToken.None);

        await new LoginPage(Page).SignInAsync(_fixture.WebBaseUrl, email, TestCredentials.KnownPassword);

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Sign in" })).ToBeVisibleAsync();
    }
}
