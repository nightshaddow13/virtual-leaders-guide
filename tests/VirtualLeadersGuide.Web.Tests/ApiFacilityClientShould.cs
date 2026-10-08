using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VirtualLeadersGuide.Web.Facilities;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="ApiActivityClientShould"/>'s shape. <see cref="ApiFacilityClient.ListAsync"/> was added
/// by P8-3 (#167); <see cref="ApiFacilityClient.GetAsync"/> and <see cref="ApiFacilityClient.UpdateAsync"/>
/// by P8-4 (#168). Response bodies are anonymous objects with already-lowercase property names, reproducing
/// Api's actual wire shape without touching <see cref="ApiFacilityClient"/>'s <see langword="internal"/>
/// envelope types.
/// </remarks>
public class ApiFacilityClientShould
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    [Fact]
    public async Task ReturnTheMappedFacilitiesAndTotal_WhenApiRespondsWithOk_ForListAsync()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { FacilityResource(firstId), FacilityResource(secondId) },
            meta = new { total = 5 }
        }));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityReadOutcome outcome, IReadOnlyList<FacilityDto> facilities, int total) =
            await client.ListAsync(1, 10, null, CancellationToken.None);

        Assert.Equal(FacilityReadOutcome.Success, outcome);
        Assert.Equal([firstId, secondId], facilities.Select(f => f.Id));
        Assert.Equal(5, total);
    }

    [Fact]
    public async Task FallBackToTheReturnedCount_WhenApiOmitsMetaTotal_ForListAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { FacilityResource() }
        }));
        ApiFacilityClient client = CreateClient(handler);

        (_, IReadOnlyList<FacilityDto> facilities, int total) =
            await client.ListAsync(1, 10, null, CancellationToken.None);

        Assert.Equal(facilities.Count, total);
    }

    [Fact]
    public async Task IncludeSortAndPageQueryParametersButNoFilter_WhenListing_ForListAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() });
        });
        ApiFacilityClient client = CreateClient(handler);

        await client.ListAsync(2, 25, null, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        string query = capturedRequest!.RequestUri!.Query;
        Assert.Contains("sort=name", query, StringComparison.Ordinal);
        Assert.Contains("page[number]=2", Uri.UnescapeDataString(query), StringComparison.Ordinal);
        Assert.Contains("page[size]=25", Uri.UnescapeDataString(query), StringComparison.Ordinal);
        Assert.DoesNotContain("filter=", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseTheCallersSort_WhenOneIsSupplied_ForListAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() });
        });
        ApiFacilityClient client = CreateClient(handler);

        await client.ListAsync(1, 10, "-name", CancellationToken.None);

        Assert.Contains("sort=-name", capturedRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnForbiddenWithAnEmptyList_WhenApiRespondsWithForbidden_ForListAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityReadOutcome outcome, IReadOnlyList<FacilityDto> facilities, int total) =
            await client.ListAsync(1, 10, null, CancellationToken.None);

        Assert.Equal(FacilityReadOutcome.Forbidden, outcome);
        Assert.Empty(facilities);
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenTheHttpCallFails_ForListAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiFacilityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(() => client.ListAsync(1, 10, null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForListAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiFacilityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(() => client.ListAsync(1, 10, null, CancellationToken.None));
    }

    [Fact]
    public async Task SendJsonApiAcceptHeaderAndTheBearerToken_WhenSendingARequest_ForCreateAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.Created, new { data = FacilityResource() });
        });
        ApiFacilityClient client = CreateClient(handler);

        await client.CreateAsync("Camp Blackhawk", Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(JsonApiMediaType, capturedRequest!.Headers.Accept.Single().MediaType);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task SendTheNameAndFacilityTypeIdAttributes_WhenCreating_ForCreateAsync()
    {
        var facilityTypeId = Guid.NewGuid();
        string? capturedBody = null;
        string? capturedContentType = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedContentType = request.Content.Headers.ContentType?.MediaType;
            return JsonApiResponse(HttpStatusCode.Created, new { data = FacilityResource(facilityTypeId: facilityTypeId) });
        });
        ApiFacilityClient client = CreateClient(handler);

        await client.CreateAsync("Camp Blackhawk", facilityTypeId, CancellationToken.None);

        Assert.Equal(JsonApiMediaType, capturedContentType);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"name\":\"Camp Blackhawk\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains($"\"facilityTypeId\":\"{facilityTypeId}\"", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheCreatedFacility_WhenApiRespondsWithCreated_ForCreateAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(
            HttpStatusCode.Created, new { data = FacilityResource(facilityId, facilityTypeId, "Camp Blackhawk") }));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityWriteOutcome outcome, FacilityDto? facility, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Camp Blackhawk", facilityTypeId, CancellationToken.None);

        Assert.Equal(FacilityWriteOutcome.Success, outcome);
        Assert.Equal(facilityId, facility?.Id);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityWriteOutcome outcome, FacilityDto? facility, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Camp Blackhawk", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FacilityWriteOutcome.Forbidden, outcome);
        Assert.Null(facility);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnInvalidWithThePointer_WhenApiRespondsWithUnprocessableEntity_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.UnprocessableEntity, new
        {
            errors = new[]
            {
                new { title = "Unknown Facility Type.", source = new { pointer = "/data/attributes/facilityTypeId" } }
            }
        }));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityWriteOutcome outcome, FacilityDto? facility, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Camp Blackhawk", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FacilityWriteOutcome.Invalid, outcome);
        Assert.Null(facility);
        Assert.Equal(["/data/attributes/facilityTypeId"], pointers);
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenTheHttpCallFails_ForCreateAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiFacilityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(
            () => client.CreateAsync("Camp Blackhawk", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiFacilityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(
            () => client.CreateAsync("Camp Blackhawk", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ReturnTheMappedFacility_WhenApiRespondsWithOk_ForGetAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(
                HttpStatusCode.OK, new { data = FacilityResource(facilityId, facilityTypeId, "Camp Blackhawk") });
        });
        ApiFacilityClient client = CreateClient(handler);

        (FacilityReadOutcome outcome, FacilityDto? facility) = await client.GetAsync(facilityId, CancellationToken.None);

        Assert.Equal(FacilityReadOutcome.Success, outcome);
        Assert.Equal(facilityId, facility?.Id);
        Assert.Equal("Camp Blackhawk", facility?.Name);
        Assert.Equal(facilityTypeId, facility?.FacilityTypeId);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        Assert.Equal($"/api/facilities/{facilityId}", capturedRequest.RequestUri!.AbsolutePath);
        Assert.NotNull(capturedRequest.Headers.Authorization);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForGetAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityReadOutcome outcome, FacilityDto? facility) = await client.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FacilityReadOutcome.Forbidden, outcome);
        Assert.Null(facility);
    }

    [Fact]
    public async Task ReturnNotFound_WhenApiRespondsWithNotFound_ForGetAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityReadOutcome outcome, FacilityDto? facility) = await client.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FacilityReadOutcome.NotFound, outcome);
        Assert.Null(facility);
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenTheHttpCallFails_ForGetAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiFacilityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(
            () => client.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForGetAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiFacilityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(
            () => client.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task PatchTheTypeIdAndAttributes_WhenUpdating_ForUpdateAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        string? capturedContentType = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedContentType = request.Content.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ApiFacilityClient client = CreateClient(handler);

        await client.UpdateAsync(facilityId, "Renamed", facilityTypeId, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, capturedRequest!.Method);
        Assert.Equal($"/api/facilities/{facilityId}", capturedRequest.RequestUri!.AbsolutePath);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal(JsonApiMediaType, capturedContentType);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"type\":\"facilities\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains($"\"id\":\"{facilityId}\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Renamed\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains($"\"facilityTypeId\":\"{facilityTypeId}\"", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnSuccess_WhenApiRespondsWithNoContent_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityWriteOutcome outcome, IReadOnlyList<string> pointers) =
            await client.UpdateAsync(Guid.NewGuid(), "Renamed", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FacilityWriteOutcome.Success, outcome);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityWriteOutcome outcome, IReadOnlyList<string> pointers) =
            await client.UpdateAsync(Guid.NewGuid(), "Renamed", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FacilityWriteOutcome.Forbidden, outcome);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnNotFound_WhenApiRespondsWithNotFound_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityWriteOutcome outcome, IReadOnlyList<string> pointers) =
            await client.UpdateAsync(Guid.NewGuid(), "Renamed", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FacilityWriteOutcome.NotFound, outcome);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnInvalidWithThePointer_WhenApiRespondsWithUnprocessableEntity_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.UnprocessableEntity, new
        {
            errors = new[]
            {
                new { title = "Unknown Facility Type.", source = new { pointer = "/data/attributes/facilityTypeId" } }
            }
        }));
        ApiFacilityClient client = CreateClient(handler);

        (FacilityWriteOutcome outcome, IReadOnlyList<string> pointers) =
            await client.UpdateAsync(Guid.NewGuid(), "Renamed", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(FacilityWriteOutcome.Invalid, outcome);
        Assert.Equal(["/data/attributes/facilityTypeId"], pointers);
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenTheHttpCallFails_ForUpdateAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiFacilityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(
            () => client.UpdateAsync(Guid.NewGuid(), "Renamed", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiFacilityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(
            () => client.UpdateAsync(Guid.NewGuid(), "Renamed", Guid.NewGuid(), CancellationToken.None));
    }

    private static ApiFacilityClient CreateClient(HttpMessageHandler apiHandler) =>
        ApiClientTestFactory.CreateFacilityClient(apiHandler);

    private static object FacilityResource(
        Guid? id = null, Guid? facilityTypeId = null, string name = "Camp Blackhawk") =>
        new
        {
            type = "facilities",
            id = (id ?? Guid.NewGuid()).ToString(),
            attributes = new { name, facilityTypeId = facilityTypeId ?? Guid.NewGuid() }
        };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        return response;
    }
}
