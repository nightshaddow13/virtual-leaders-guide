using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Positive and negative coverage for <c>/api/facilities</c> (P8-2, #166): <c>FacilityResourceDefinition</c>'s
/// Read-for-any-signed-in-caller, Write-Admin-only scoping (ADR-0070). Unlike <see cref="InfoPagesResourceShould"/>,
/// a Director's Read succeeds regardless of Event assignment - a Facility has no owning Event to check
/// assignment against - but every write verb 403s for a Director the same way <see cref="EventsResourceShould"/>
/// already does for Event details. Grants are simulated entirely via pre-formatted role claims, not real
/// <c>UserRoles</c> rows, matching <see cref="InfoPagesResourceShould"/>'s pattern.
/// </remarks>
public class FacilitiesResourceShould : IAsyncLifetime
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
    public async Task SucceedWithCreated_WhenAdminCreatesAFacility_ForPost()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "facilities",
                attributes = new { name = "Camp Blackhawk", facilityTypeId = facilityType.Id }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/facilities", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement attributes = await AttributesOfAsync(response);
        Assert.Equal("Camp Blackhawk", attributes.GetProperty("name").GetString());
        Assert.Equal(facilityType.Id.ToString(), attributes.GetProperty("facilityTypeId").GetString());
    }

    [Fact]
    public async Task SucceedWithOk_WhenAdminReadsAFacility_ForGetSingle()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/facilities/{facility.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminUpdatesAFacility_ForPatch()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new { type = "facilities", id = facility.Id.ToString(), attributes = new { name = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/facilities/{facility.Id}", body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SucceedWithNoContent_WhenAdminDeletesAFacility_ForDelete()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/facilities/{facility.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReturnEveryFacility_WhenAdminListsFacilities_ForGetCollection()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility first = await _factory.CreateFacilityAsync(facilityType.Id);
        Facility second = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/facilities");

        string[] ids = await IdsOfAsync(response);
        Assert.Contains(first.Id.ToString(), ids);
        Assert.Contains(second.Id.ToString(), ids);
    }

    [Fact]
    public async Task SucceedWithOk_WhenADirectorReadsAFacility_ForGetSingle()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/facilities/{facility.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReturnEveryFacility_WhenADirectorListsFacilities_ForGetCollection()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/facilities");

        string[] ids = await IdsOfAsync(response);
        Assert.Contains(facility.Id.ToString(), ids);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorCreatesAFacility_ForPost()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = DirectorClient();
        var body = new
        {
            data = new
            {
                type = "facilities",
                attributes = new { name = "Camp Blackhawk", facilityTypeId = facilityType.Id }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/facilities", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorUpdatesAFacility_ForPatch()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = DirectorClient();
        var body = new
        {
            data = new { type = "facilities", id = facility.Id.ToString(), attributes = new { name = "Renamed" } }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Patch, $"/api/facilities/{facility.Id}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenADirectorDeletesAFacility_ForDelete()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = DirectorClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/facilities/{facility.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsListsFacilities_ForGetCollection()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, "/api/facilities");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsReadsAFacility_ForGetSingle()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = _factory.CreateUserClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/facilities/{facility.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithForbidden_WhenACallerWithNoRoleClaimsCreatesAFacility_ForPost()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        using HttpClient client = _factory.CreateUserClient();
        var body = new
        {
            data = new
            {
                type = "facilities",
                attributes = new { name = "Camp Blackhawk", facilityTypeId = facilityType.Id }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/facilities", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithUnprocessableEntity_WhenAdminCreatesAFacilityOnAnUnknownFacilityType_ForPost()
    {
        using HttpClient client = AdminClient();
        var body = new
        {
            data = new
            {
                type = "facilities",
                attributes = new { name = "Camp Blackhawk", facilityTypeId = Guid.NewGuid() }
            }
        };

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, "/api/facilities", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorPointersAsync(response, "/data/attributes/facilityTypeId");
    }

    [Fact]
    public async Task RejectWithNotFound_WhenAdminReadsANonexistentFacility_ForGetSingle()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/facilities/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectWithNotFound_WhenAdminDeletesANonexistentFacility_ForDelete()
    {
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, $"/api/facilities/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NotExposeRelationships_WhenReadingAFacility_ForGetSingle()
    {
        FacilityType facilityType = await _factory.CreateFacilityTypeAsync();
        Facility facility = await _factory.CreateFacilityAsync(facilityType.Id);
        using HttpClient client = AdminClient();

        HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, $"/api/facilities/{facility.Id}");

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.False(data.TryGetProperty("relationships", out _));
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
