using System.Net;
using System.Net.Http.Headers;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// <c>Role</c> (<c>VirtualLeadersGuide.Api.Data</c>) is a plain POCO, not <c>Identifiable&lt;T&gt;</c>, so it
/// isn't reachable as a JSON:API resource (ADR-0017's Consequences, unchanged by ADR-0033). <c>Event</c> was
/// the same until P2-7 (#16) turned it into a resource - see <c>EventsResourceShould</c> for its positive
/// coverage. <c>UserRole</c> was the same until P2-8 (#17; ADR-0033) turned it into a resource, exposed at
/// <c>/api/roleGrants</c> (not <c>/api/userRoles</c> - see <c>UserRole</c>'s <c>[Resource(PublicName = ...)]</c>)
/// - see <c>RoleGrantsResourceShould</c> for its positive coverage.
/// </remarks>
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

    [Fact]
    public async Task ReturnNotFound_WhenRequestingRolesAsAJsonApiResource_ForGetCollection()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/roles");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
