using System.Security.Claims;
using System.Text;
using Azure.Storage.Blobs;
using JsonApiDotNetCore.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VirtualLeadersGuide.Api;
using VirtualLeadersGuide.Api.Authorization;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Api.Identity;
using VirtualLeadersGuide.Identity.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<VirtualLeadersGuideDbContext>("virtualleadersguide");

builder.AddPasscodeDataProtection();

builder.AddInternalAuthentication();
builder.Services.AddInternalAuthorization();

builder.Services.AddJsonApi<VirtualLeadersGuideDbContext>(options =>
{
    options.Namespace = "api";
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddResourceDefinition<EventResourceDefinition>();
builder.Services.AddResourceDefinition<UserRoleResourceDefinition>();

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

if (builder.Configuration.GetValue<bool>("Migrations:ApplyAutomatically"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>()
        .Database.Migrate();
}

app.UseJsonApi();
app.MapControllers().RequireAuthorization(InternalJwtDefaults.PolicyName);
app.MapInternalIdentityEndpoints();
app.MapInternalAuthorizationEndpoints();

app.MapDefaultEndpoints();

app.Run();

/// <summary>
/// Entry point type for <c>VirtualLeadersGuide.Api</c>, exposed as <see langword="partial"/> so
/// <c>WebApplicationFactory&lt;Program&gt;</c> can bootstrap this app in-process for integration tests
/// (see <c>ApiWebApplicationFactory</c>).
/// </summary>
public partial class Program;

/// <summary>Bootstrapping for <c>VirtualLeadersGuide.Api</c>'s <c>Program.cs</c>.</summary>
internal static class ApiProgramExtensions
{
    /// <remarks>
    /// Api's own Data Protection key ring, isolated from Web's — separate application name, separate blob
    /// key file in the same container, so neither app can decrypt data the other protected. See ADR-0026
    /// for why, and P2-2 (#11) / <c>docs/runbooks/p2-2-blob-dataprotection-keys.md</c> for the
    /// blob-persistence mechanics this reuses.
    /// </remarks>
    internal static void AddPasscodeDataProtection(this WebApplicationBuilder builder)
    {
        builder.AddAzureBlobServiceClient("blobs");
        builder.Services.AddDataProtection()
            .SetApplicationName("VirtualLeadersGuide.Api")
            .PersistKeysToAzureBlobStorage(services =>
                services.GetRequiredService<BlobServiceClient>()
                    .GetBlobContainerClient("dataprotection-keys")
                    .GetBlobClient("api-keys.xml"));
    }

    /// <remarks>
    /// Composes the <c>X-Internal-Key</c> scheme (ADR-0015) with a stock <c>JwtBearer</c> handler validating
    /// the internal JWT Web mints (ADR-0007) — see <see cref="AddInternalAuthorization"/> for how the two
    /// compose under one policy.
    /// </remarks>
    internal static void AddInternalAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(InternalApiKeyDefaults.AuthenticationScheme)
            .AddScheme<InternalApiKeyAuthenticationOptions, InternalApiKeyAuthenticationHandler>(
                InternalApiKeyDefaults.AuthenticationScheme, options => { })
            .AddJwtBearer(
                InternalJwtDefaults.AuthenticationScheme,
                options => ConfigureInternalJwtBearer(options, builder.Configuration));
    }

    /// <remarks>
    /// A 5-minute-lived token (<see cref="InternalJwtDefaults.Lifetime"/>) under the 5-minute *default*
    /// clock skew would never actually be treated as expired — <c>ClockSkew</c> is shortened here to
    /// something that only absorbs clock drift between Web and Api, not the token's entire lifetime.
    /// <c>IssuerSigningKey</c> is left <see langword="null"/> (rather than throwing here) when unconfigured,
    /// so validation fails closed per token — <c>AuthenticateAsync</c> returns a failed result, matching
    /// <see cref="InternalApiKeyAuthenticationHandler"/>'s posture for a missing key, rather than the app
    /// crashing at startup. <c>AuthenticationType</c> is set explicitly because <c>JwtBearer</c> otherwise
    /// defaults an unset value to <c>AuthenticationTypes.Federation</c>, not the scheme name — the assertion
    /// in <see cref="AddInternalAuthorization"/> needs to tell a JWT-issued identity apart from the
    /// claims-free <c>X-Internal-Key</c> identity reliably.
    /// </remarks>
    private static void ConfigureInternalJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        string? signingKey = configuration[InternalJwtDefaults.SigningKeyConfigurationKey];

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = InternalJwtDefaults.Issuer,
            ValidateAudience = true,
            ValidAudience = InternalJwtDefaults.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = string.IsNullOrEmpty(signingKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
            AuthenticationType = InternalJwtDefaults.AuthenticationScheme
        };
    }

    /// <remarks>
    /// The fallback policy (every endpoint, including anything added later — ADR-0015) stays the loose
    /// floor: any authenticated identity. <see cref="InternalJwtDefaults.PolicyName"/> is the stricter,
    /// opt-in policy applied only to the JSON:API resource surface (<c>MapControllers</c>) — see ADR-0015's
    /// amendment for why it isn't the fallback, why <c>/internal/identity/*</c> and
    /// <c>/internal/authorization/*</c> stay off it, and why a request with <c>X-Internal-Key</c> but no
    /// valid JWT gets 403 rather than 401. Requires no role claim to be present — "zero roles" is a valid,
    /// authenticated identity that simply can't do anything yet.
    /// </remarks>
    internal static void AddInternalAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(InternalJwtDefaults.PolicyName, policy => policy
                .AddAuthenticationSchemes(
                    InternalApiKeyDefaults.AuthenticationScheme, InternalJwtDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.Identities
                    .Any(identity => identity.AuthenticationType == InternalJwtDefaults.AuthenticationScheme)));
    }
}
