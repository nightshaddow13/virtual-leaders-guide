using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Response bodies are built as anonymous objects with already-lowercase property names, so
/// <see cref="System.Net.Http.Json.JsonContent"/>'s default serialization reproduces Api's actual wire
/// shape without needing access to <see cref="ApiEventClient"/>'s <see langword="internal"/> envelope
/// types - the same approach <c>EventsResourceShould</c> uses for request bodies on the Api side.
/// </remarks>
public class ApiEventClientShould
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    [Fact]
    public async Task SendJsonApiAcceptHeaderAndTheBearerToken_WhenSendingARequest_ForGetEventAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.OK, new { data = EventResource() });
        });
        ApiEventClient client = CreateClient(handler);

        await client.GetEventAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(JsonApiMediaType, capturedRequest!.Headers.Accept.Single().MediaType);
        Assert.NotNull(capturedRequest.Headers.Authorization);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task ReturnTheMappedEventsAndTotal_WhenApiRespondsWithOk_ForGetEventsAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { EventResource(eventId, "Fall Camporee", "fall-camporee") },
            meta = new { total = 4 }
        }));
        ApiEventClient client = CreateClient(handler);

        (IReadOnlyList<EventDto> events, int total) = await client.GetEventsAsync(1, 10, null, CancellationToken.None);

        Assert.Single(events);
        Assert.Equal(eventId, events[0].Id);
        Assert.Equal("Fall Camporee", events[0].Name);
        Assert.Equal(4, total);
    }

    [Fact]
    public async Task ReturnTheEventsCountAsTotal_WhenApiOmitsMeta_ForGetEventsAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.OK, new
        {
            data = new[] { EventResource(), EventResource() }
        }));
        ApiEventClient client = CreateClient(handler);

        (IReadOnlyList<EventDto> events, int total) = await client.GetEventsAsync(1, 10, null, CancellationToken.None);

        Assert.Equal(2, events.Count);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task IncludePageAndSortQueryParameters_WhenListingEvents_ForGetEventsAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonApiResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() });
        });
        ApiEventClient client = CreateClient(handler);

        await client.GetEventsAsync(2, 25, "-name", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        string query = capturedRequest!.RequestUri!.Query;
        Assert.Contains("page[number]=2", query, StringComparison.Ordinal);
        Assert.Contains("page[size]=25", query, StringComparison.Ordinal);
        Assert.Contains("sort=-name", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheMappedEvent_WhenApiRespondsWithOk_ForGetEventAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(
            _ => JsonApiResponse(HttpStatusCode.OK, new { data = EventResource(eventId) }));
        ApiEventClient client = CreateClient(handler);

        (EventReadOutcome outcome, EventDto? @event) = await client.GetEventAsync(eventId, CancellationToken.None);

        Assert.Equal(EventReadOutcome.Success, outcome);
        Assert.Equal(eventId, @event?.Id);
    }

    [Fact]
    public async Task ReturnTheMappedDates_WhenApiRespondsWithOk_ForGetEventAsync()
    {
        var eventId = Guid.NewGuid();
        var startsAt = new DateTimeOffset(2026, 6, 12, 14, 0, 0, TimeSpan.Zero);
        var endsAt = new DateTimeOffset(2026, 6, 14, 22, 0, 0, TimeSpan.Zero);
        var handler = new StubHttpMessageHandler(
            _ => JsonApiResponse(HttpStatusCode.OK, new { data = EventResource(eventId, startsAt: startsAt, endsAt: endsAt) }));
        ApiEventClient client = CreateClient(handler);

        (EventReadOutcome outcome, EventDto? @event) = await client.GetEventAsync(eventId, CancellationToken.None);

        Assert.Equal(EventReadOutcome.Success, outcome);
        Assert.Equal(startsAt, @event?.StartsAt);
        Assert.Equal(endsAt, @event?.EndsAt);
    }

    [Fact]
    public async Task ReturnNullDates_WhenApiRespondsWithNoDatesSet_ForGetEventAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(
            _ => JsonApiResponse(HttpStatusCode.OK, new { data = EventResource(eventId) }));
        ApiEventClient client = CreateClient(handler);

        (_, EventDto? @event) = await client.GetEventAsync(eventId, CancellationToken.None);

        Assert.Null(@event?.StartsAt);
        Assert.Null(@event?.EndsAt);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForGetEventAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiEventClient client = CreateClient(handler);

        (EventReadOutcome outcome, EventDto? @event) = await client.GetEventAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(EventReadOutcome.Forbidden, outcome);
        Assert.Null(@event);
    }

    [Fact]
    public async Task SendOnlyTheNameAttribute_WhenCreatingAnEventWithNoDates_ForCreateAsync()
    {
        string? capturedBody = null;
        string? capturedContentType = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedContentType = request.Content.Headers.ContentType?.MediaType;
            return JsonApiResponse(HttpStatusCode.Created, new { data = EventResource() });
        });
        ApiEventClient client = CreateClient(handler);

        await client.CreateAsync("Fall Camporee", null, null, CancellationToken.None);

        Assert.Equal(JsonApiMediaType, capturedContentType);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"name\":\"Fall Camporee\"", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("slug", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("passcode", capturedBody, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Pins ADR-0042's clearing mechanism: unlike every other attribute here, an unset
    /// <see cref="EventAttributesDto.StartsAt"/>/<see cref="EventAttributesDto.EndsAt"/> still serializes as
    /// an explicit JSON <c>null</c> rather than being omitted - see <see cref="EventAttributesDto"/>'s
    /// remarks. This is the first thing to break if that attribute's <c>JsonIgnoreCondition.Never</c> is
    /// ever "tidied" back to the type default.
    /// </remarks>
    [Fact]
    public async Task SendExplicitNullDates_WhenCreatingAnEventWithNoDates_ForCreateAsync()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonApiResponse(HttpStatusCode.Created, new { data = EventResource() });
        });
        ApiEventClient client = CreateClient(handler);

        await client.CreateAsync("Fall Camporee", null, null, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"startsAt\":null", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"endsAt\":null", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendBothDatesAsUtcIso8601_WhenCreatingAnEventWithDates_ForCreateAsync()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonApiResponse(HttpStatusCode.Created, new { data = EventResource() });
        });
        ApiEventClient client = CreateClient(handler);
        var startsAt = new DateTimeOffset(2026, 6, 12, 14, 0, 0, TimeSpan.Zero);
        var endsAt = new DateTimeOffset(2026, 6, 14, 22, 0, 0, TimeSpan.Zero);

        await client.CreateAsync("Fall Camporee", startsAt, endsAt, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"startsAt\":\"2026-06-12T14:00:00", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"endsAt\":\"2026-06-14T22:00:00", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnTheCreatedEvent_WhenApiRespondsWithCreated_ForCreateAsync()
    {
        var eventId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(
            HttpStatusCode.Created, new { data = EventResource(eventId, "Fall Camporee", "fall-camporee") }));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, EventDto? @event, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Fall Camporee", null, null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Success, outcome);
        Assert.Equal(eventId, @event?.Id);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, EventDto? @event, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Fall Camporee", null, null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Forbidden, outcome);
        Assert.Null(@event);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnBothConflictPointers_WhenApiRespondsWithConflict_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.Conflict, new
        {
            errors = new[]
            {
                new { title = "Resource conflict.", source = new { pointer = "/data/attributes/name" } },
                new { title = "Resource conflict.", source = new { pointer = "/data/attributes/slug" } }
            }
        }));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, EventDto? @event, IReadOnlyList<string> pointers) =
            await client.CreateAsync("Fall Camporee", null, null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Conflict, outcome);
        Assert.Null(@event);
        Assert.Equal(
            new[] { "/data/attributes/name", "/data/attributes/slug" }.Order(),
            pointers.Order());
    }

    [Fact]
    public async Task ReturnInvalidWithThePointer_WhenApiRespondsWithUnprocessableEntity_ForCreateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.UnprocessableEntity, new
        {
            errors = new[]
            {
                new { title = "Invalid date range.", source = new { pointer = "/data/attributes/endsAt" } }
            }
        }));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, EventDto? @event, IReadOnlyList<string> pointers) = await client.CreateAsync(
            "Fall Camporee", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Invalid, outcome);
        Assert.Null(@event);
        Assert.Equal(["/data/attributes/endsAt"], pointers);
    }

    [Fact]
    public async Task SendOnlyProvidedAttributes_WhenUpdatingAnEventWithSomeFieldsOmitted_ForUpdateAsync()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ApiEventClient client = CreateClient(handler);

        await client.UpdateAsync(
            Guid.NewGuid(), name: null, slug: "fall-camporee", passcode: null,
            startsAt: null, endsAt: null, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"slug\":\"fall-camporee\"", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("name", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("passcode", capturedBody, StringComparison.Ordinal);
    }

    /// <remarks>See <see cref="SendExplicitNullDates_WhenCreatingAnEventWithNoDates_ForCreateAsync"/> - the same clearing mechanism, exercised on PATCH.</remarks>
    [Fact]
    public async Task SendExplicitNullDates_WhenClearingBothDates_ForUpdateAsync()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ApiEventClient client = CreateClient(handler);

        await client.UpdateAsync(
            Guid.NewGuid(), name: null, slug: null, passcode: null,
            startsAt: null, endsAt: null, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"startsAt\":null", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"endsAt\":null", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnSuccess_WhenApiRespondsWithNoContent_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, IReadOnlyList<string> pointers) = await client.UpdateAsync(
            Guid.NewGuid(), "Renamed", null, null, null, null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Success, outcome);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, IReadOnlyList<string> pointers) = await client.UpdateAsync(
            Guid.NewGuid(), "Renamed", null, null, null, null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Forbidden, outcome);
        Assert.Empty(pointers);
    }

    [Fact]
    public async Task ReturnTheConflictPointer_WhenApiRespondsWithConflict_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.Conflict, new
        {
            errors = new[] { new { title = "Resource conflict.", source = new { pointer = "/data/attributes/slug" } } }
        }));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, IReadOnlyList<string> pointers) = await client.UpdateAsync(
            Guid.NewGuid(), null, "taken-slug", null, null, null, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Conflict, outcome);
        Assert.Equal(["/data/attributes/slug"], pointers);
    }

    [Fact]
    public async Task ReturnInvalidWithThePointer_WhenApiRespondsWithUnprocessableEntity_ForUpdateAsync()
    {
        var handler = new StubHttpMessageHandler(_ => JsonApiResponse(HttpStatusCode.UnprocessableEntity, new
        {
            errors = new[]
            {
                new { title = "Invalid date range.", source = new { pointer = "/data/attributes/startsAt" } }
            }
        }));
        ApiEventClient client = CreateClient(handler);

        (EventWriteOutcome outcome, IReadOnlyList<string> pointers) = await client.UpdateAsync(
            Guid.NewGuid(), null, null, null, null, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Invalid, outcome);
        Assert.Equal(["/data/attributes/startsAt"], pointers);
    }

    [Fact]
    public async Task SendADeleteRequestToTheEventsIdUri_WhenDeleting_ForDeleteAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        Guid id = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        ApiEventClient client = CreateClient(handler);

        await client.DeleteAsync(id, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Delete, capturedRequest.Method);
        Assert.EndsWith($"/api/events/{id}", capturedRequest.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnSuccess_WhenApiRespondsWithNoContent_ForDeleteAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        ApiEventClient client = CreateClient(handler);

        EventWriteOutcome outcome = await client.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Success, outcome);
    }

    [Fact]
    public async Task ReturnForbidden_WhenApiRespondsWithForbidden_ForDeleteAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        ApiEventClient client = CreateClient(handler);

        EventWriteOutcome outcome = await client.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(EventWriteOutcome.Forbidden, outcome);
    }

    [Fact]
    public async Task ReturnNotFound_WhenApiRespondsWithNotFound_ForDeleteAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        ApiEventClient client = CreateClient(handler);

        EventWriteOutcome outcome = await client.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(EventWriteOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task ThrowEventDataUnavailableException_WhenTheHttpCallFails_ForDeleteAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiEventClient client = CreateClient(handler);

        await Assert.ThrowsAsync<EventDataUnavailableException>(
            () => client.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ThrowEventDataUnavailableException_WhenTheHttpCallFails_ForGetEventsAsync()
    {
        var handler = StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"));
        ApiEventClient client = CreateClient(handler);

        await Assert.ThrowsAsync<EventDataUnavailableException>(
            () => client.GetEventsAsync(1, 10, null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowEventDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForGetEventsAsync()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        ApiEventClient client = CreateClient(handler);

        await Assert.ThrowsAsync<EventDataUnavailableException>(
            () => client.GetEventsAsync(1, 10, null, CancellationToken.None));
    }

    private static ApiEventClient CreateClient(HttpMessageHandler apiHandler) =>
        ApiClientTestFactory.CreateEventClient(apiHandler);

    private static object EventResource(
        Guid? id = null, string name = "Fall Camporee", string slug = "fall-camporee",
        DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null) =>
        new
        {
            type = "events",
            id = (id ?? Guid.NewGuid()).ToString(),
            attributes = new { name, slug, passcode = "TigerLantern", startsAt, endsAt }
        };

    private static HttpResponseMessage JsonApiResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        return response;
    }
}
