using System.Net;
using System.Net.Http.Headers;

namespace VirtualLeadersGuide.Api.Tests;

/// <summary>
/// Shared scaffolding for a test class that proves a set of entities are deliberately not reachable as
/// JSON:API resources (plain POCOs, not <c>Identifiable&lt;T&gt;</c>) - see
/// <see cref="DomainAuthorizationEntitiesAreNotJsonApiResourcesShould"/> and
/// <see cref="PageEntitiesAreNotJsonApiResourcesShould"/> for the two current uses.
/// </summary>
public abstract class NonResourceEntityShouldBase : IAsyncLifetime
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

    /// <summary>Asserts that requesting <paramref name="requestUri"/> as a JSON:API resource returns 404.</summary>
    protected async Task AssertNotFoundAsync(string requestUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
