using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace VirtualLeadersGuide.E2E.Tests;

// The smoke test proving the whole arrangement works (P2.1-1, #59): AspireE2ECollection's constructor
// injection and Microsoft.Playwright.Xunit's own IAsyncLifetime chain (see PageTest -> ContextTest ->
// BrowserTest -> PlaywrightTest -> WorkerAwareTest -> ExceptionCapturer) don't conflict - xUnit resolves the
// fixture purely by matching this constructor's parameter type, independently of the base class's own
// IAsyncLifetime hooks, which still run afterward exactly as they would with no fixture involved.
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
}
