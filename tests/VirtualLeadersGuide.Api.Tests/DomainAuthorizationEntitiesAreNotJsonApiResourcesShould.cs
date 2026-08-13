using System.Net;
using System.Net.Http.Headers;

namespace VirtualLeadersGuide.Api.Tests;

// Role, UserRole, and Event (VirtualLeadersGuide.Api.Data) are plain POCOs, not Identifiable<T>, so none are
// reachable as a JSON:API resource yet - see VirtualLeadersGuideDbContext's comment. userRoles is expected
// to flip once P2-8 (#17) deliberately turns UserRole into a resource; events is expected to flip once P2-7
// (#16) does the same for Event; roles has no ticket planning to expose it at all.
public class DomainAuthorizationEntitiesAreNotJsonApiResourcesShould : IAsyncLifetime
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
    [InlineData("/api/roles")]
    [InlineData("/api/userRoles")]
    [InlineData("/api/events")]
    public async Task ReturnNotFound_WhenRequestingADomainAuthorizationTableAsAJsonApiResource_ForGetCollection(
        string requestUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
