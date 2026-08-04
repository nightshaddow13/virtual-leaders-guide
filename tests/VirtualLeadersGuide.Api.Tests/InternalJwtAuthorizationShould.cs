using System.Net;
using System.Net.Http.Headers;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

// Pins P2-5's (#14) authorization policy: /api/* requires a valid internal JWT alongside X-Internal-Key
// (ADR-0007), composed as the RequireInternalUser policy (see the amendment in
// docs/adr/0015-internal-key-validated-via-authentication-handler.md). Uses /api/users as the concrete
// resource, since it's the one JSON:API resource that exists today reachable without any prerequisite state
// (ADR-0024) - SmokeTestEntitiesEndpointShould already covers the "happy path reaches the resource pipeline"
// case end to end.
//
// Two distinct failure status codes appear below, both correct: 401 when there's no authenticated identity
// at all (nothing to challenge against), and 403 when X-Internal-Key authenticated the caller as "this is
// Web" but the RequireInternalUser policy's JWT-identity requirement still isn't met - an authorization
// failure on top of a real authentication, not an authentication failure.
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
        // No authenticated identity at all - the framework challenges (401), rather than forbidding (403),
        // since there's nothing to be "forbidden" from: authentication itself never happened.
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenXInternalKeyIsPresentButNoBearerTokenIs_ForGetUsers()
    {
        // X-Internal-Key alone authenticates the caller as "this is Web" - just not as the RequireInternalUser
        // policy's required JWT identity, so this is an authorization failure (403), not an authentication
        // one (401): the caller proved *something*, just not the specific thing this policy requires.
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
        // Deliberately zero role claims: RequireInternalUser proves identity only - it doesn't gate on role
        // possession. Deciding what a role permits is P2-6/P2-7/P2-8's job, not this policy's.
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
