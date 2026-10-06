using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VirtualLeadersGuide.Web.Facilities;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="ApiFacilityClientShould"/>'s shape, covering both
/// <see cref="ApiFacilityTypeClient.ListAsync"/> and <see cref="ApiFacilityTypeClient.CreateAsync"/>
/// (P8-2, #166). Response bodies are anonymous objects with already-lowercase property names, reproducing
/// Api's actual wire shape without touching <see cref="ApiFacilityTypeClient"/>'s <see langword="internal"/>
/// envelope types.
/// </remarks>
public class ApiFacilityTypeClientShould
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    [Fact]
    public async Task ReturnEveryFacilityType_WhenApiRespondsWithOk_ForListAsync()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { FacilityTypeResource(firstId, "Camp"), FacilityTypeResource(secondId, "Retreat Center") }
        }));
        ApiFacilityTypeClient client = CreateClient(handler);

        (FacilityTypeReadOutcome outcome, IReadOnlyList<FacilityTypeDto> types) =
            await client.ListAsync(CancellationToken.None);

        Assert.Equal(FacilityTypeReadOutcome.Success, outcome);
        Assert.Equal([firstId, secondId], types.Select(t => t.Id));
    }

    [Fact]
    public async Task ReturnForbiddenWithAnEmptyList_WhenApiRespondsWithForbidden_ForListAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiFacilityTypeClient client = CreateClient(handler);

        (FacilityTypeReadOutcome outcome, IReadOnlyList<FacilityTypeDto> types) =
            await client.ListAsync(CancellationToken.None);

        Assert.Equal(FacilityTypeReadOutcome.Forbidden, outcome);
        Assert.Empty(types);
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenTheHttpCallFails_ForListAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiFacilityTypeClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(() => client.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SendTheNameAttribute_WhenCreating_ForCreateAsync()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonApiResponse(HttpStatusCode.Created, new { data = FacilityTypeResource(name: "Camp") });
        });
        ApiFacilityTypeClient client = CreateClient(handler);

        await client.CreateAsync("Camp", CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"name\":\"Camp\"", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheCreatedFacilityType_WhenApiRespondsWithCreated_ForCreateAsync()
    {
        var typeId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(
            HttpStatusCode.Created, new { data = FacilityTypeResource(typeId, "Camp") }));
        ApiFacilityTypeClient client = CreateClient(handler);

        (FacilityTypeWriteOutcome outcome, FacilityTypeDto? facilityType) =
            await client.CreateAsync("Camp", CancellationToken.None);

        Assert.Equal(FacilityTypeWriteOutcome.Success, outcome);
        Assert.Equal(typeId, facilityType?.Id);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiFacilityTypeClient client = CreateClient(handler);

        (FacilityTypeWriteOutcome outcome, FacilityTypeDto? facilityType) =
            await client.CreateAsync("Camp", CancellationToken.None);

        Assert.Equal(FacilityTypeWriteOutcome.Forbidden, outcome);
        Assert.Null(facilityType);
    }

    [Fact]
    public async Task ReturnConflict_WhenApiRespondsWithConflict_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        ApiFacilityTypeClient client = CreateClient(handler);

        (FacilityTypeWriteOutcome outcome, FacilityTypeDto? facilityType) =
            await client.CreateAsync("Camp", CancellationToken.None);

        Assert.Equal(FacilityTypeWriteOutcome.Conflict, outcome);
        Assert.Null(facilityType);
    }

    [Fact]
    public async Task ThrowFacilityDataUnavailableException_WhenTheHttpCallFails_ForCreateAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiFacilityTypeClient client = CreateClient(handler);

        await Assert.ThrowsAsync<FacilityDataUnavailableException>(
            () => client.CreateAsync("Camp", CancellationToken.None));
    }

    private static ApiFacilityTypeClient CreateClient(HttpMessageHandler apiHandler) =>
        ApiClientTestFactory.CreateFacilityTypeClient(apiHandler);

    private static object FacilityTypeResource(Guid? id = null, string name = "Camp") => new
    {
        type = "facilityTypes",
        id = (id ?? Guid.NewGuid()).ToString(),
        attributes = new { name }
    };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        return response;
    }
}
