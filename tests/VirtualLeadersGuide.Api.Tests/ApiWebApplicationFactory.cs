using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VirtualLeadersGuide.Api;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <summary>
/// In-process <see cref="Program"/> host over an in-memory SQLite database (ADR-0014), backing every
/// <c>Api.Tests</c> integration test class.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>The <c>X-Internal-Key</c> value this factory configures <c>Api</c> to expect.</summary>
    public const string InternalApiKey = "test-internal-api-key";

    /// <summary>The internal-JWT signing key this factory configures <c>Api</c> to validate against.</summary>
    public const string InternalJwtKey = "test-internal-jwt-signing-key-at-least-32-bytes-long";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <remarks>
    /// <see cref="Event.Passcode"/>'s converter needs a real <see cref="IDataProtectionProvider"/> to be
    /// registered (<c>VirtualLeadersGuideDbContext.BuildPasscodeConverter</c> fails closed otherwise) -
    /// redirected to this local temp directory so nothing ever reaches real Blob Storage.
    /// </remarks>
    private readonly string _dataProtectionKeysDirectory =
        Path.Combine(Path.GetTempPath(), "vlg-api-tests-keys-" + Guid.NewGuid());

    /// <remarks>
    /// <c>Program.cs</c>'s top-level statements call <c>AddAzureBlobServiceClient("blobs")</c>
    /// unconditionally, before <see cref="ConfigureWebHost"/>'s <c>ConfigureAppConfiguration</c> hook gets a
    /// chance to apply (the same ordering constraint <c>SignInShould</c>/<c>DashboardShould</c> hit in
    /// <c>Web.Tests</c>) - an environment variable is read by <c>WebApplication.CreateBuilder</c>'s own
    /// default configuration sources from the very first line, sidestepping that ordering question entirely.
    /// Set once here in a static constructor rather than per-instance: unlike <c>Web.Tests</c>' per-test
    /// set/reset (which swaps in different fakes per scenario), this value never changes across
    /// <c>Api.Tests</c>, so there's nothing to race and no assembly-level parallelization opt-out needed.
    /// The value itself is never dialed - Data Protection persistence is redirected to
    /// <see cref="_dataProtectionKeysDirectory"/> before anything touches it.
    /// </remarks>
    static ApiWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__blobs",
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Removing EF Core's pooled <see cref="VirtualLeadersGuideDbContext"/> registration
    /// (<c>AddSqlServerDbContext</c>'s <c>AddDbContextPool</c>) isn't just <c>RemoveAll&lt;T&gt;</c>: pooling
    /// also registers <c>DbContextOptions&lt;T&gt;</c> as singleton plus internal pool-lease services closed
    /// over the DbContext type that would otherwise be left behind expecting a pooled registration -
    /// every descriptor closed over the DbContext type has to go before re-adding a plain (non-pooled)
    /// <c>AddDbContext</c> for SQLite (ADR-0014). Also replaces <c>Program.cs</c>'s blob-backed Data
    /// Protection registration with a file-system one rooted at
    /// <see cref="_dataProtectionKeysDirectory"/>, same shape as <c>SignInShould</c>/<c>DashboardShould</c>
    /// in <c>Web.Tests</c>.
    /// </remarks>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalApi:Key"] = InternalApiKey,
                [InternalJwtDefaults.SigningKeyConfigurationKey] = InternalJwtKey
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<VirtualLeadersGuideDbContext>();

            foreach (ServiceDescriptor descriptor in services
                .Where(descriptor => descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericArguments().Contains(typeof(VirtualLeadersGuideDbContext)))
                .ToList())
            {
                services.Remove(descriptor);
            }

            _connection.Open();

            services.AddDbContext<VirtualLeadersGuideDbContext>(options =>
                options.UseSqlite(_connection));

            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_dataProtectionKeysDirectory));
        });
    }

    /// <summary>Creates the SQLite schema fresh, for tests that need to read/write real rows.</summary>
    public async Task InitializeDatabaseAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Creates and persists a new <see cref="Event"/> via <see cref="Event.Create"/> - for tests (P2-6, #15)
    /// that need a real row satisfying <c>UserRoles.EventId</c>'s foreign key, rather than just an in-memory
    /// object.
    /// </summary>
    /// <param name="name">
    /// The Event's Name. Omit to get a fresh Guid-suffixed default (and the Slug <see cref="Event.Create"/>
    /// derives from it), so repeated calls within one test don't collide on the Name/Slug unique indexes.
    /// </param>
    /// <returns>The newly persisted <see cref="Event"/>.</returns>
    public async Task<Event> CreateEventAsync(string? name = null)
    {
        using IServiceScope scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Event @event = Event.Create(name ?? $"Event {Guid.NewGuid()}");

        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync();
        return @event;
    }

    /// <summary>
    /// An <see cref="HttpClient"/> carrying only <c>X-Internal-Key</c> - what a genuine <c>/internal/*</c>
    /// caller sends (ADR-0022/0024), and what the JSON:API resource surface used to accept alone before this
    /// ticket. Use <see cref="CreateUserClient"/> for <c>/api/*</c> calls now that they additionally require
    /// a valid internal JWT.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(InternalApiKeyDefaults.HeaderName, InternalApiKey);
        return client;
    }

    /// <summary>
    /// An <see cref="HttpClient"/> carrying both <c>X-Internal-Key</c> and a freshly-signed internal JWT
    /// (P2-5, #14) - what a real <c>Web</c> request to <c>/api/*</c> sends. Matches production shape: Web's
    /// <c>InternalApiKeyHandler</c> and <c>InternalApiClient</c> stack the same way.
    /// </summary>
    /// <param name="userId">The <c>sub</c>/<see cref="ClaimTypes.NameIdentifier"/> claim value. Defaults to a
    /// fresh <see cref="Guid"/> when omitted - most tests only care about the token being valid, not whose it is.</param>
    /// <param name="roleClaims">
    /// Zero or more pre-formatted <see cref="ClaimTypes.Role"/> claim values (see <see cref="RoleClaimValue.Format"/>)
    /// to include. Omit entirely to mint a token with no role claims - a deliberately valid case (see
    /// <c>InternalJwtAuthorizationShould</c>), since this policy proves identity only, not role possession.
    /// </param>
    public HttpClient CreateUserClient(string? userId = null, params string[] roleClaims)
    {
        HttpClient client = CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MintToken(userId, roleClaims));
        return client;
    }

    /// <summary>
    /// Mints a signed internal JWT using the same shape <c>Api</c> validates against
    /// (<see cref="InternalJwtDefaults"/>), for tests exercising invalid tokens (wrong key, wrong
    /// issuer/audience, expired) that <see cref="CreateUserClient"/> can't produce.
    /// </summary>
    public static string MintToken(
        string? userId = null,
        IEnumerable<string>? roleClaims = null,
        string? signingKey = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId ?? Guid.NewGuid().ToString()) };
        if (roleClaims is not null)
        {
            claims.AddRange(roleClaims.Select(claim => new Claim(ClaimTypes.Role, claim)));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? InternalJwtDefaults.Issuer,
            Audience = audience ?? InternalJwtDefaults.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.Add(InternalJwtDefaults.Lifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? InternalJwtKey)),
                SecurityAlgorithms.HmacSha256)
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();

            if (Directory.Exists(_dataProtectionKeysDirectory))
            {
                Directory.Delete(_dataProtectionKeysDirectory, recursive: true);
            }
        }

        base.Dispose(disposing);
    }
}
