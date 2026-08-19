using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

public class InternalJwtProviderShould
{
    private const string SigningKey = "test-internal-jwt-signing-key-at-least-32-bytes-long";
    private const string UserId = "user-1";

    [Fact]
    public async Task MintOneRoleClaimPerGrant_WhenGrantsExist_ForGetTokenAsync()
    {
        List<RoleGrantDto> grants =
        [
            new() { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin, EventId = null },
            new() { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = Guid.NewGuid() }
        ];
        InternalJwtProvider provider = CreateProvider(grants);

        string token = await provider.GetTokenAsync(CancellationToken.None);

        ClaimsPrincipal principal = await ValidateAsync(token);
        List<string> roleClaims = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Equal(2, roleClaims.Count);
        Assert.Contains(RoleClaimValue.Format(grants[0]), roleClaims);
        Assert.Contains(RoleClaimValue.Format(grants[1]), roleClaims);
        Assert.Equal(UserId, principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task FormatAnEventScopedGrantAsRoleNameColonEventId_WhenMintingAToken_ForGetTokenAsync()
    {
        var grant = new RoleGrantDto
        {
            Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = Guid.NewGuid()
        };
        InternalJwtProvider provider = CreateProvider([grant]);

        string token = await provider.GetTokenAsync(CancellationToken.None);

        ClaimsPrincipal principal = await ValidateAsync(token);
        Assert.Equal($"{RoleNames.Director}:{grant.EventId}", principal.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task MintAZeroRoleToken_WhenTheUserRowNoLongerExistsOnApi_ForGetTokenAsync()
    {
        InternalJwtProvider provider = CreateProvider(grants: null);

        string token = await provider.GetTokenAsync(CancellationToken.None);

        ClaimsPrincipal principal = await ValidateAsync(token);
        Assert.Empty(principal.FindAll(ClaimTypes.Role));
        Assert.Equal(UserId, principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task ReturnTheCachedToken_WhenCalledAgainWithinTheRefreshWindow_ForGetTokenAsync()
    {
        var grantLookupCount = 0;
        InternalJwtProvider provider = CreateProvider([], () => grantLookupCount++);

        string first = await provider.GetTokenAsync(CancellationToken.None);
        string second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, grantLookupCount);
    }

    [Fact]
    public async Task PerformAFreshGrantLookup_WhenTheCachedTokenIsWithinRefreshSkewOfExpiry_ForGetTokenAsync()
    {
        var grantLookupCount = 0;
        InternalJwtProvider provider = CreateProvider([], () => grantLookupCount++);

        await provider.GetTokenAsync(CancellationToken.None);
        SetCachedTokenExpiresAt(provider, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10));

        await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(2, grantLookupCount);
    }

    [Fact]
    public async Task PropagateAuthorizationDataUnavailableException_WhenApiIsUnreachable_ForGetTokenAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"))));
        var provider = new InternalJwtProvider(new FixedAuthenticationStateProvider(UserId), client, Configuration());

        await Assert.ThrowsAsync<AuthorizationDataUnavailableException>(
            () => provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ThrowInvalidOperationException_WhenTheCircuitHasNoSignedInUser_ForGetTokenAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
        var provider = new InternalJwtProvider(new FixedAuthenticationStateProvider(userId: null), client, Configuration());

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ThrowInvalidOperationException_WhenTheSigningKeyIsNotConfigured_ForGetTokenAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
        var provider = new InternalJwtProvider(
            new FixedAuthenticationStateProvider(UserId), client, new ConfigurationBuilder().Build());

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync(CancellationToken.None));
    }

    private static InternalJwtProvider CreateProvider(IReadOnlyList<RoleGrantDto>? grants, Action? onGrantsRequested = null)
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            onGrantsRequested?.Invoke();
            return grants is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(grants) };
        });

        var client = new ApiRoleGrantClient(new StubHttpClientFactory(handler));
        return new InternalJwtProvider(new FixedAuthenticationStateProvider(UserId), client, Configuration());
    }

    /// <remarks>
    /// <see cref="InternalJwtProvider"/> has no injected clock seam (deliberately - see its own remarks:
    /// lazy, inline, no background timer, matching ADR-0007). Waiting out the real ~5-minute lifetime isn't
    /// practical in a test, so this reaches into the cached-expiry field directly to simulate "almost
    /// expired" rather than actually waiting.
    /// </remarks>
    private static void SetCachedTokenExpiresAt(InternalJwtProvider provider, DateTimeOffset expiresAt)
    {
        FieldInfo field = typeof(InternalJwtProvider)
            .GetField("_cachedTokenExpiresAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(provider, expiresAt);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [InternalJwtDefaults.SigningKeyConfigurationKey] = SigningKey
        })
        .Build();

    /// <remarks>
    /// Validates against the same <see cref="TokenValidationParameters"/> shape Api's <c>Program.cs</c>
    /// configures, proving the minted token is one Api would actually accept - without spinning up a full
    /// Api host.
    /// </remarks>
    private static async Task<ClaimsPrincipal> ValidateAsync(string token)
    {
        var handler = new JsonWebTokenHandler();
        TokenValidationResult result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = InternalJwtDefaults.Issuer,
            ValidateAudience = true,
            ValidAudience = InternalJwtDefaults.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        return new ClaimsPrincipal(result.ClaimsIdentity);
    }
}

/// <summary>Returns a fixed <see cref="AuthenticationState"/> - a stand-in for the real per-circuit provider.</summary>
internal sealed class FixedAuthenticationStateProvider(string? userId) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsIdentity identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}
