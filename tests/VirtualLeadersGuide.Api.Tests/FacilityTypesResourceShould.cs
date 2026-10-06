using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Positive and negative coverage for <c>/api/facilityTypes</c> (P8-2, #166): every verb, Read included, is
/// Admin-only (ADR-0071) - unlike <see cref="FacilitiesResourceShould"/>, a Director gets a flat 403 on
/// every shape, not a silently-narrowed collection, since this resource has nothing to narrow *to*. Grants
/// are simulated entirely via pre-formatted role claims, not real <c>UserRoles</c> rows, matching
/// <see cref="InfoPagesResourceShould"/>'s pattern.
/// </remarks>
public class FacilityTypesResourceShould : IAsyncLifetime
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task SucceedWithCreated_WhenAdminCreatesAFacilityType_ForPost()
    {
        using HttpClient client = AdminClient();
        var body = new { data = new { type = "facilityTypes", attributes = new { name = "Camp" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/facilityTypes", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.Equal("Camp", attributes.GetProperty("name").GetString());
    }

    [Fact]
    public async Task SucceedWithOk_WhenAdminReadsAFacilityType_ForGetSingle()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/facilityTypes/{facilityType.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminUpdatesAFacilityType_ForPatch()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "facilityTypes", id = facilityType.Id.ToString(), attributes = new { name = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/facilityTypes/{facilityType.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminDeletesAFacilityType_ForDelete()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/facilityTypes/{facilityType.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReturnEveryFacilityType_WhenAdminListsFacilityTypes_ForGetCollection()
    {
        FacilityType first = await _factory.CreateFacilityTypeAsync();
        FacilityType second = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/facilityTypes");

        string[] ids = await IdsOfAsync(response);
        Assert.Contains(first.Id.ToString(), ids);
        Assert.Contains(second.Id.ToString(), ids);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorListsFacilityTypes_ForGetCollection()
    {
        await _factory.CreateFacilityTypeAsync();
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/facilityTypes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorReadsAFacilityType_ForGetSingle()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/facilityTypes/{facilityType.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorCreatesAFacilityType_ForPost()
    {
        using HttpClient client = DirectorClient();
        var body = new { data = new { type = "facilityTypes", attributes = new { name = "Camp" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/facilityTypes", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorUpdatesAFacilityType_ForPatch()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = DirectorClient();
        var body = new
        {
            data = new { type = "facilityTypes", id = facilityType.Id.ToString(), attributes = new { name = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/facilityTypes/{facilityType.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorDeletesAFacilityType_ForDelete()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/facilityTypes/{facilityType.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsListsFacilityTypes_ForGetCollection()
    {
        await _factory.CreateFacilityTypeAsync();
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/facilityTypes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithConflict_WhenAdminCreatesADuplicateNameCaseInsensitively_ForPost()
    {
        await _factory.CreateFacilityTypeAsync("Camp");
        using HttpClient client = AdminClient();
        var body = new { data = new { type = "facilityTypes", attributes = new { name = "CAMP" } } };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/facilityTypes", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/name");
    }

    private HttpClient AdminClient() =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.AdminRoleClaim()]);

    private HttpClient DirectorClient() =>
        _factory.CreateUserClient(roleClaims: [ApiWebApplicationFactory.DirectorRoleClaim(Guid.NewGuid())]);

    private static async Task AssertErrorPointersAsync(HttpResponseMessage response, params string[] expectedPointers)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] actualPointers = document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("source").GetProperty("pointer").GetString()!)
            .ToArray();
        Assert.Equal(expectedPointers.Order(), actualPointers.Order());
    }

    private static async Task<JsonElement> AttributesOfAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("attributes").Clone();
    }

    private static async Task<string[]> IdsOfAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToArray();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string requestUri, object? body = null)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        }

        return await client.SendAsync(request);
    }
}
