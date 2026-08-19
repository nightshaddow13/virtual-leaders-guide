using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace VirtualLeadersGuide.Api.Tests;

public class SmokeTestEntitiesEndpointShould : IAsyncLifetime
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    private ApiWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    /// <remarks>
    /// Uses <see cref="ApiWebApplicationFactory.CreateUserClient"/>, not just
    /// <see cref="ApiWebApplicationFactory.CreateAuthenticatedClient"/> - <c>/api/*</c> requires a valid
    /// internal JWT in addition to <c>X-Internal-Key</c> (P2-5, #14), and this class is the "a valid JWT
    /// reaches the resource pipeline" acceptance test.
    /// </remarks>
    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateUserClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task SucceedWithValidJsonApiResponses_WhenPerformingFullCrudLifecycle_ForSmokeTestEntitiesEndpoint()
    {
        var createBody = new
        {
            data = new
            {
                type = "smokeTestEntities",
                attributes = new { name = "Smoke Test" }
            }
        };

        HttpResponseMessage createResponse = await SendAsync(HttpMethod.Post, "/api/smokeTestEntities", createBody);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using JsonDocument createdDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        string id = createdDocument.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        HttpResponseMessage readResponse = await SendAsync(HttpMethod.Get, $"/api/smokeTestEntities/{id}");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var updateBody = new
        {
            data = new
            {
                type = "smokeTestEntities",
                id,
                attributes = new { name = "Updated Smoke Test" }
            }
        };

        HttpResponseMessage updateResponse = await SendAsync(HttpMethod.Patch, $"/api/smokeTestEntities/{id}", updateBody);
        Assert.True(updateResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent);

        HttpResponseMessage deleteResponse = await _client.DeleteAsync($"/api/smokeTestEntities/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage verifyResponse = await SendAsync(HttpMethod.Get, $"/api/smokeTestEntities/{id}");
        Assert.Equal(HttpStatusCode.NotFound, verifyResponse.StatusCode);
    }

    /// <remarks>
    /// JSON:API content negotiation rejects a Content-Type with parameters (e.g. the charset
    /// <see cref="StringContent"/>'s 3-arg constructor would add) with 415, so the header is set explicitly
    /// below instead of going through that overload.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, object? body = null)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        }

        return await _client.SendAsync(request);
    }
}
