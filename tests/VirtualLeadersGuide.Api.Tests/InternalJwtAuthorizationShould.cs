using System.Net;
using System.Net.Http.Headers;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Pins P2-5's (#14) authorization policy: <c>/api/*</c> requires a valid internal JWT alongside
/// <c>X-Internal-Key</c> (ADR-0007), composed as the <c>RequireInternalUser</c> policy - see ADR-0015's
/// amendment, including for why 401 vs. 403 differ below. Uses <c>/api/users</c> as the concrete resource,
/// since it's the one JSON:API resource that exists today reachable without any prerequisite state
/// (ADR-0024) - <c>SmokeTestEntitiesEndpointShould</c> already covers the "happy path reaches the resource
/// pipeline" case end to end.
/// </remarks>
public class InternalJwtAuthorizationShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task ReturnUnauthorized_WhenNeitherXInternalKeyNorABearerTokenIsPresent_ForGetUsers()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenXInternalKeyIsPresentButNoBearerTokenIs_ForGetUsers()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenTheBearerTokenIsExpired_ForGetUsers()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiWebApplicationFactory.MintToken(expires: DateTime.UtcNow.AddMinutes(-1)));

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenTheBearerTokenIsSignedWithTheWrongKey_ForGetUsers()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiWebApplicationFactory.MintToken(signingKey: "a-completely-different-signing-key-value"));

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenTheBearerTokenHasTheWrongIssuer_ForGetUsers()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiWebApplicationFactory.MintToken(issuer: "not-vlg-web"));

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenTheBearerTokenHasTheWrongAudience_ForGetUsers()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiWebApplicationFactory.MintToken(audience: "not-vlg-api"));

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenTheBearerTokenIsValid_ForGetUsers()
    {
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenTheBearerTokenCarriesNoRoleClaims_ForGetUsers()
    {
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenTheBearerTokenCarriesRoleClaims_ForGetUsers()
    {
        using HttpClient client = _factory.CreateUserClient(roleClaims: [RoleNames.Admin]);

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
