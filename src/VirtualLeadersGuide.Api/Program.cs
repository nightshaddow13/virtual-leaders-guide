using System.Security.Claims;
using System.Text;
using JsonApiDotNetCore.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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

builder.Services.AddAuthentication(InternalApiKeyDefaults.AuthenticationScheme)
    .AddScheme<InternalApiKeyAuthenticationOptions, InternalApiKeyAuthenticationHandler>(
        InternalApiKeyDefaults.AuthenticationScheme, options => { })
    // Validates the internal JWT Web mints to forward a signed-in user's identity (ADR-0007, P2-5/#14),
    // alongside - not instead of - the X-Internal-Key scheme above. Stock JwtBearer middleware rather than
    // hand-rolled validation, per ADR-0007.
    .AddJwtBearer(InternalJwtDefaults.AuthenticationScheme, options =>
    {
        string? signingKey = builder.Configuration[InternalJwtDefaults.SigningKeyConfigurationKey];

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = InternalJwtDefaults.Issuer,
            ValidateAudience = true,
            ValidAudience = InternalJwtDefaults.Audience,
            ValidateLifetime = true,
            // A 5-minute-lived token (InternalJwtDefaults.Lifetime) under the 5-minute *default* clock skew
            // would never actually be treated as expired - shortened here to something that only absorbs
            // clock drift between Web and Api, not the token's entire lifetime.
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            // Left null (rather than throwing here) when unconfigured, so validation fails closed per token
            // - AuthenticateAsync returns a failed result, matching InternalApiKeyAuthenticationHandler's
            // posture for a missing InternalApi:Key - rather than the app crashing at startup.
            IssuerSigningKey = string.IsNullOrEmpty(signingKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
            // JwtBearer defaults an unset AuthenticationType to AuthenticationTypes.Federation, not the
            // scheme name - set explicitly so the RequireInternalUser policy below can reliably tell a
            // JWT-issued identity apart from the claims-free X-Internal-Key identity.
            AuthenticationType = InternalJwtDefaults.AuthenticationScheme
        };
    });

builder.Services.AddAuthorizationBuilder()
    // Unchanged: the loose floor every endpoint gets by default, including anything added later (ADR-0015).
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    // Stricter, opt-in policy applied only to the JSON:API resource surface below (MapControllers) - proves
    // a valid internal JWT is present, not just X-Internal-Key. Deliberately not required on
    // /internal/identity/* or /internal/authorization/*: those run before a user token can exist (they back
    // sign-in itself, and the latter is what produces this token's claims in the first place) - see the
    // amendment in docs/adr/0015-internal-key-validated-via-authentication-handler.md.
    //
    // RequireAuthenticatedUser() alone would pass on the X-Internal-Key identity too (it's "any listed
    // scheme succeeded"), so the assertion below is load-bearing: it demands the JWT scheme specifically.
    // Deliberately does not require any role claim to be present - "zero roles" is a valid, authenticated
    // identity that simply can't do anything yet; deciding what a role permits is P2-6/P2-7/P2-8's job.
    .AddPolicy(InternalJwtDefaults.PolicyName, policy => policy
        .AddAuthenticationSchemes(InternalApiKeyDefaults.AuthenticationScheme, InternalJwtDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireAssertion(context => context.User.Identities
            .Any(identity => identity.AuthenticationType == InternalJwtDefaults.AuthenticationScheme)));

builder.Services.AddJsonApi<VirtualLeadersGuideDbContext>(options =>
{
    options.Namespace = "api";
});

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
// JsonApiDotNetCore's generated controllers are the only controllers in the app, so this is the entire
// /api/* surface and nothing else - /internal/* endpoints below are minimal APIs, unaffected by
// MapControllers, and stay on the fallback policy alone.
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
