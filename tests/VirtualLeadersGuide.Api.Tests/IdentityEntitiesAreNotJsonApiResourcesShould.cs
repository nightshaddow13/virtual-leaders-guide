using System.Net;
using System.Net.Http.Headers;

namespace VirtualLeadersGuide.Api.Tests;

// JsonApiDotNetCore only picks up entities implementing IIdentifiable (see SmokeTestEntity) - the Identity
// entities IdentityDbContext<ApplicationUser> adds don't, so they should never be reachable as a JSON:API
// resource under /api. This asserts that stays true rather than trusting it - see ADR-0022.
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
    [InlineData("/api/users")]
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
