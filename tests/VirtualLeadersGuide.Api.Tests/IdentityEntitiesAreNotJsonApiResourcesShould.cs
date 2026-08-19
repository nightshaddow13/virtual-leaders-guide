using System.Net;
using System.Net.Http.Headers;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// JsonApiDotNetCore only picks up entities implementing <c>IIdentifiable</c> (see <c>SmokeTestEntity</c>) -
/// the Identity entities <c>IdentityDbContext&lt;ApplicationUser&gt;</c> adds don't, so they should never be
/// reachable as a JSON:API resource under <c>/api</c>. This asserts that stays true rather than trusting it
/// (ADR-0022). <c>ApplicationUser</c> itself is the one exception - it does implement
/// <c>IIdentifiable&lt;string&gt;</c> and is reachable at <c>/api/users</c> (ADR-0024), so that case moved
/// to <c>UsersResourceShould</c>, which additionally proves credential columns stay unreachable there. Every
/// other case here still asserts a real, permanent 404.
/// </remarks>
public class IdentityEntitiesAreNotJsonApiResourcesShould : IAsyncLifetime
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    private ApiWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateAuthenticatedClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Theory]
    [InlineData("/api/applicationUsers")]
    [InlineData("/api/identityUsers")]
    [InlineData("/api/identityRoles")]
    public async Task ReturnNotFound_WhenRequestingAnIdentityTableAsAJsonApiResource_ForGetCollection(string requestUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
