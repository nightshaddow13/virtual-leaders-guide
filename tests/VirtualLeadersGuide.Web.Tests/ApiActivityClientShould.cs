using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="ApiInfoPageClientShould"/>'s shape, limited to <see cref="ApiActivityClient.CreateAsync"/>
/// - the only method <see cref="ApiActivityClient"/> exposes yet (P5-6, #87). Response bodies are anonymous
/// objects with already-lowercase property names, reproducing Api's actual wire shape without touching
/// <see cref="ApiActivityClient"/>'s <see langword="internal"/> envelope types.
/// </remarks>
public class ApiActivityClientShould
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    [Fact]
    public async Task SendJsonApiAcceptHeaderAndTheBearerToken_WhenSendingARequest_ForCreateAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.Created, new { data = ActivityResource() });
        });
        ApiActivityClient client = CreateClient(handler);

        await client.CreateAsync(Guid.NewGuid(), "Canoe Basics", "content", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(JsonApiMediaType, capturedRequest!.Headers.Accept.Single().MediaType);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task SendTheEventIdNameAndDescriptionAttributes_WhenCreating_ForCreateAsync()
    {
        var eventId = Guid.NewGuid();
        string? capturedBody = null;
        string? capturedContentType = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedContentType = request.Content.Headers.ContentType?.MediaType;
            return JsonApiResponse(HttpStatusCode.Created, new { data = ActivityResource(eventId: eventId) });
        });
        ApiActivityClient client = CreateClient(handler);

        await client.CreateAsync(eventId, "Canoe Basics", "Paddle strokes and the buddy system.", CancellationToken.None);

        Assert.Equal(JsonApiMediaType, capturedContentType);
        Assert.NotNull(capturedBody);
        Assert.Contains($"\"eventId\":\"{eventId}\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Canoe Basics\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"description\":\"Paddle strokes and the buddy system.\"", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheCreatedActivity_WhenApiRespondsWithCreated_ForCreateAsync()
    {
        var activityId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(
            HttpStatusCode.Created, new { data = ActivityResource(activityId, eventId, "Canoe Basics") }));
        ApiActivityClient client = CreateClient(handler);

        (ActivityWriteOutcome outcome, ActivityDto? activity, IReadOnlyList<string> pointers) =
            await client.CreateAsync(eventId, "Canoe Basics", "content", CancellationToken.None);

        Assert.Equal(ActivityWriteOutcome.Success, outcome);
        Assert.Equal(activityId, activity?.Id);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiActivityClient client = CreateClient(handler);

        (ActivityWriteOutcome outcome, ActivityDto? activity, IReadOnlyList<string> pointers) =
            await client.CreateAsync(Guid.NewGuid(), "Canoe Basics", "content", CancellationToken.None);

        Assert.Equal(ActivityWriteOutcome.Forbidden, outcome);
        Assert.Null(activity);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnInvalidWithThePointer_WhenApiRespondsWithUnprocessableEntity_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.UnprocessableEntity, new
        {
            errors = new[]
            {
                new { title = "Unknown Event.", source = new { pointer = "/data/attributes/eventId" } }
            }
        }));
        ApiActivityClient client = CreateClient(handler);

        (ActivityWriteOutcome outcome, ActivityDto? activity, IReadOnlyList<string> pointers) =
            await client.CreateAsync(Guid.NewGuid(), "Canoe Basics", "content", CancellationToken.None);

        Assert.Equal(ActivityWriteOutcome.Invalid, outcome);
        Assert.Null(activity);
        Assert.Equal(["/data/attributes/eventId"], pointers);
    }

    [Fact]
    public async Task ThrowActivityDataUnavailableException_WhenTheHttpCallFails_ForCreateAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiActivityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<ActivityDataUnavailableException>(
            () => client.CreateAsync(Guid.NewGuid(), "Canoe Basics", "content", CancellationToken.None));
    }

    [Fact]
    public async Task ThrowActivityDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiActivityClient client = CreateClient(handler);

        await Assert.ThrowsAsync<ActivityDataUnavailableException>(
            () => client.CreateAsync(Guid.NewGuid(), "Canoe Basics", "content", CancellationToken.None));
    }

    private static ApiActivityClient CreateClient(HttpMessageHandler apiHandler) =>
        ApiClientTestFactory.CreateActivityClient(apiHandler);

    private static object ActivityResource(
        Guid? id = null, Guid? eventId = null, string name = "Canoe Basics", string description = "content") =>
        new
        {
            type = "activities",
            id = (id ?? Guid.NewGuid()).ToString(),
            attributes = new { eventId = eventId ?? Guid.NewGuid(), name, description }
        };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        return response;
    }
}
