using System.Net;
using System.Net.Http.Headers;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Pins P2-5's (#14) authorization policy: <c>/api/*</c> requires a valid internal JWT alongside
/// <c>X-Internal-Key</c> (ADR-0007), composed as the <c>RequireInternalUser</c> policy - see ADR-0015's
/// amendment, including for why 401 vs. 403 differ below. Uses <c>GET /api/events</c> as the concrete
/// resource: unlike <c>/api/users</c> (Admin-gated since P2-12, #43 - see <c>ApplicationUserResourceDefinition</c>),
/// a collection read here always succeeds regardless of role claims - <c>EventResourceDefinition</c>
/// silently narrows a non-Admin's results (possibly to empty) rather than rejecting the request (ADR-0031)
/// - so a plain 200 here demonstrates the JWT policy alone, uncorfounded by any resource-level gate.
/// <c>SmokeTestEntitiesEndpointShould</c> already covers the "happy path reaches the resource pipeline" case
/// end to end.
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
    public async Task ReturnUnauthorized_WhenNeitherXInternalKeyNorABearerTokenIsPresent_ForGetEvents()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenXInternalKeyIsPresentButNoBearerTokenIs_ForGetEvents()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenTheBearerTokenIsExpired_ForGetEvents()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiWebApplicationFactory.MintToken(expires: DateTime.UtcNow.AddMinutes(-1)));

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenTheBearerTokenIsSignedWithTheWrongKey_ForGetEvents()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiWebApplicationFactory.MintToken(signingKey: "a-completely-different-signing-key-value"));

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenTheBearerTokenHasTheWrongIssuer_ForGetEvents()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiWebApplicationFactory.MintToken(issuer: "not-vlg-web"));

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnForbidden_WhenTheBearerTokenHasTheWrongAudience_ForGetEvents()
    {
        using HttpClient client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiWebApplicationFactory.MintToken(audience: "not-vlg-api"));

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenTheBearerTokenIsValid_ForGetEvents()
    {
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenTheBearerTokenCarriesNoRoleClaims_ForGetEvents()
    {
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithOk_WhenTheBearerTokenCarriesRoleClaims_ForGetEvents()
    {
        using HttpClient client = _factory.CreateUserClient(roleClaims: [RoleNames.Admin]);

        HttpResponseMessage response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
