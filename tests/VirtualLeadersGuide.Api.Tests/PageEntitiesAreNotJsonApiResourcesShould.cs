using System.Net;
using System.Net.Http.Headers;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// <c>Page</c>/<c>InfoPage</c>/<c>PageType</c> (<c>VirtualLeadersGuide.Api.Data</c>, P5-15, #20) are plain
/// POCOs, not <c>Identifiable&lt;T&gt;</c>, so none is reachable as a JSON:API resource - deliberately, since
/// <c>AddJsonApi&lt;TDbContext&gt;</c> walks every entity type in the EF model and would auto-register any
/// <c>IIdentifiable</c> type it finds regardless of a missing <c>[Resource]</c> attribute (see <c>Page</c>'s
/// remarks). P5-16 (#21) is what turns <c>InfoPage</c> into a resource - see the pattern
/// <c>DomainAuthorizationEntitiesAreNotJsonApiResourcesShould</c> already set for <c>Event</c>/<c>UserRole</c>
/// going the other way.
/// </remarks>
public class PageEntitiesAreNotJsonApiResourcesShould : IAsyncLifetime
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
    [InlineData("/api/pages")]
    [InlineData("/api/infoPages")]
    [InlineData("/api/pageTypes")]
    public async Task ReturnNotFound_WhenRequestingAsAJsonApiResource_ForGetCollection(string requestUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
