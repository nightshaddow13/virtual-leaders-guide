using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Tests;

/// <summary>
/// The role-claim-present counterpart to <see cref="DashboardShould"/> - same prerender-redirect mechanism
/// (see that class's header comment), but with a signed-in principal carrying a <see cref="ClaimTypes.Role"/>
/// claim, proving <c>Dashboard.razor</c>'s existing "any role at all" check now passes once P2-5 (#14) is
/// stamping that claim, with no change needed to the component itself.
/// </summary>
public class DashboardWithRoleClaimShould : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private readonly string _dataProtectionKeysDirectory =
        Path.Combine(Path.GetTempPath(), "vlg-web-tests-keys-" + Guid.NewGuid());

    /// <remarks>
    /// See <see cref="SignInShould"/>'s remarks on this same override. <c>_factory.Services</c> is touched
    /// below to force the host to build now, while the env var is still set - see <see cref="DisposeAsync"/>.
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
                services.AddAuthentication(RoleClaimTestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, RoleClaimTestAuthHandler>(
                        RoleClaimTestAuthHandler.SchemeName, _ => { });

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
    public async Task RenderNormally_WhenTheSignedInUserHoldsARoleClaim_ForDashboard()
    {
        using HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        HttpResponseMessage response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// Always authenticates the request as a fixed user carrying one platform-wide <see cref="ClaimTypes.Role"/>
/// claim (<see cref="RoleNames.Admin"/>) - see <see cref="DashboardWithRoleClaimShould"/>'s header comment.
/// </summary>
internal sealed class RoleClaimTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestWithRole";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "test-user@example.com"),
            new Claim(ClaimTypes.Role, RoleClaimValue.Format(
                new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin, EventId = null }))
        ], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
