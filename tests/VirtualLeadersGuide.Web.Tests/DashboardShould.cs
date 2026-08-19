using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// <c>/dashboard</c> is Blazor Interactive Server - the initial HTTP response is a static server-side
/// prerender of the page (before the client's SignalR circuit connects), and that prerender pass runs
/// <c>Dashboard.razor</c>'s <c>OnInitializedAsync</c>, including its <c>NavigationManager.NavigateTo</c>
/// call. Blazor converts a <c>NavigateTo</c> during prerendering into a real HTTP redirect - the same
/// mechanism <c>RedirectToLogin.razor</c> already relies on for anonymous users - so a plain HTTP GET is
/// enough to exercise the "no role yet" branch without a live circuit.
/// </remarks>
public class DashboardShould : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private readonly string _dataProtectionKeysDirectory =
        Path.Combine(Path.GetTempPath(), "vlg-web-tests-keys-" + Guid.NewGuid());

    /// <remarks>
    /// See <see cref="SignInShould"/>'s remarks on this same override for why an environment variable, not
    /// <c>ConfigureAppConfiguration</c>, is needed here. Swaps in an always-authenticated, zero-role
    /// principal (<see cref="TestAuthHandler"/>) in place of real cookie auth, so this test exercises
    /// <c>Dashboard.razor</c>'s own role check rather than the sign-in flow (already covered by
    /// <see cref="SignInShould"/>). <c>_factory.Services</c> is touched below to force the host to build
    /// now, while the env var is still set - see <see cref="DisposeAsync"/>.
    /// </remarks>
    public Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__blobs",
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_dataProtectionKeysDirectory));
            });
        });

        _ = _factory.Services;

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("ConnectionStrings__blobs", null);

        if (Directory.Exists(_dataProtectionKeysDirectory))
        {
            Directory.Delete(_dataProtectionKeysDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task RedirectToNoAccess_WhenTheSignedInUserHoldsNoRoleClaim_ForDashboard()
    {
        using HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        HttpResponseMessage response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Account/NoAccess", response.Headers.Location?.ToString());
    }
}

/// <remarks>
/// Always authenticates the request as a fixed user with no role claims at all - see
/// <see cref="DashboardShould"/>'s remarks for why.
/// </remarks>
internal sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "test-user@example.com")
        ], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
