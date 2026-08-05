using Microsoft.Playwright;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Wraps the rendered <c>Account/Login</c> form (<c>Login.razor</c>) - the only page object in this project;
/// every other page/component this project's tests touch is driven with one-off inline Playwright calls (see
/// each test class's own header comment).
/// </summary>
public sealed class LoginPage(IPage page)
{
    /// <summary>Navigates to <c>Account/Login</c>, optionally with a <c>ReturnUrl</c> query parameter.</summary>
    /// <param name="webBaseUrl">The <c>web</c> resource's base URL (<see cref="AspireE2EFixture.WebBaseUrl"/>).</param>
    /// <param name="returnUrl">
    /// A relative return URL to append as <c>?ReturnUrl=...</c>, or <see langword="null"/> to omit it.
    /// </param>
    public async Task GotoAsync(Uri webBaseUrl, string? returnUrl = null)
    {
        var loginUri = new Uri(webBaseUrl, "Account/Login");
        string target = returnUrl is null
            ? loginUri.ToString()
            : $"{loginUri}?ReturnUrl={Uri.EscapeDataString(returnUrl)}";

        await page.GotoAsync(target);
    }

    /// <summary>Fills in and submits the Login form with the given credentials.</summary>
    public async Task SubmitAsync(string email, string password)
    {
        await page.Locator("#Input\\.Email").FillAsync(email);
        await page.Locator("#Input\\.Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Log in" }).ClickAsync();
    }
}
