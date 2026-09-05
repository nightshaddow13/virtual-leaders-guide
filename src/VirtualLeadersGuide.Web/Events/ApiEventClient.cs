using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Events;

/// <summary>Thin HTTP client over Api's <c>/api/events</c> JSON:API resource (P2-7, #16).</summary>
/// <remarks>
/// Mirrors <c>Authorization.ApiRoleGrantClient</c>'s shape - typed outcomes for expected non-2xx responses
/// rather than exceptions, <see cref="EventDataUnavailableException"/> for everything else. Differs from it
/// in two ways that matter: requests go through <see cref="InternalApiClient"/>, not a bare
/// <c>IHttpClientFactory.CreateClient("Api")</c>, because <c>/api/*</c> requires the internal JWT
/// (<c>InternalJwtDefaults.PolicyName</c>) that only <see cref="InternalApiClient"/> attaches; and every
/// request/response body is JSON:API's <c>application/vnd.api+json</c> envelope
/// (<see cref="EventDocument"/>/<see cref="EventCollectionDocument"/>/<see cref="ErrorDocument"/>), not a
/// plain DTO.
/// </remarks>
public sealed class ApiEventClient(InternalApiClient apiClient)
{
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const string EventsPath = "/api/events";
    private const string ResourceType = "events";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Lists Events visible to the caller, one page at a time.</summary>
    /// <param name="pageNumber">The 1-based page to fetch.</param>
    /// <param name="pageSize">The number of Events per page.</param>
    /// <param name="sort">
    /// A JSON:API sort expression (e.g. <c>"name"</c> or <c>"-name"</c>), or <see langword="null"/> for
    /// Api's default ordering.
    /// </param>
    /// <param name="status">
    /// Which Statuses to include - defaults to <see cref="EventStatusFilter.Current"/> (Draft plus
    /// not-yet-elapsed Live), matching the Dashboard's default view.
    /// </param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// The page of Events, and the total count of Events visible to the caller across all pages - never
    /// forbidden: an Admin sees every Event, a Director's collection is silently narrowed to their assigned
    /// Events (possibly empty) rather than denied (ADR-0031).
    /// </returns>
    public async Task<(IReadOnlyList<EventDto> Events, int Total)> GetEventsAsync(
        int pageNumber, int pageSize, string? sort, EventStatusFilter status, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, BuildCollectionUri(pageNumber, pageSize, sort, status));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        EventCollectionDocument document = await ReadAsync<EventCollectionDocument>(response, cancellationToken);
        var events = document.Data.Select(ToDto).ToList();
        return (events, document.Meta?.Total ?? events.Count);
    }

    /// <summary>Reads a single Event by id.</summary>
    /// <param name="id">The Event's id.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="EventReadOutcome.Success"/> with the Event, or <see cref="EventReadOutcome.Forbidden"/> if
    /// the caller can't read it (an unassigned Director, or the Event doesn't exist - Api doesn't
    /// distinguish the two, ADR-0031).
    /// </returns>
    public async Task<(EventReadOutcome Outcome, EventDto? Event)> GetEventAsync(
        Guid id, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, $"{EventsPath}/{id}");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (EventReadOutcome.Forbidden, null);
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        EventDocument document = await ReadAsync<EventDocument>(response, cancellationToken);
        return (EventReadOutcome.Success, ToDto(document.Data));
    }

    /// <summary>Creates a new Event with a Name and optional Start/End - Api derives the Slug and generates the Passcode.</summary>
    /// <param name="name">The new Event's display name.</param>
    /// <param name="startsAt">The new Event's start, or <see langword="null"/> to leave it unset.</param>
    /// <param name="endsAt">The new Event's end, or <see langword="null"/> to leave it unset.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="EventWriteOutcome.Success"/> with the created Event (its server-derived Slug and
    /// generated Passcode included); <see cref="EventWriteOutcome.Forbidden"/> if the caller isn't an Admin
    /// (ADR-0031); <see cref="EventWriteOutcome.Conflict"/> with the colliding attribute pointers if
    /// <paramref name="name"/> or its derived Slug is already in use; or <see cref="EventWriteOutcome.Invalid"/>
    /// with the offending pointer if <paramref name="endsAt"/> isn't a valid end for <paramref name="startsAt"/>
    /// (ADR-0042).
    /// </returns>
    public async Task<(EventWriteOutcome Outcome, EventDto? Event, IReadOnlyList<string> ConflictPointers)> CreateAsync(
        string name, DateTimeOffset? startsAt, DateTimeOffset? endsAt, CancellationToken cancellationToken)
    {
        var body = new EventDocument
        {
            Data = new EventResourceObject
            {
                Type = ResourceType,
                Attributes = new EventAttributesDto { Name = name, StartsAt = startsAt, EndsAt = endsAt }
            }
        };
        using var request = NewRequest(HttpMethod.Post, EventsPath, body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (EventWriteOutcome.Forbidden, null, []);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (EventWriteOutcome.Conflict, null, await ReadErrorPointersAsync(response, cancellationToken));
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return (EventWriteOutcome.Invalid, null, await ReadErrorPointersAsync(response, cancellationToken));
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        EventDocument created = await ReadAsync<EventDocument>(response, cancellationToken);
        return (EventWriteOutcome.Success, ToDto(created.Data), []);
    }

    /// <summary>Updates an existing Event's details. Admin-only (ADR-0031) - a Director's write always comes back Forbidden.</summary>
    /// <param name="id">The Event to update.</param>
    /// <param name="name">The new Name, or <see langword="null"/> to leave it unchanged.</param>
    /// <param name="slug">The new Slug, or <see langword="null"/> to leave it unchanged.</param>
    /// <param name="passcode">The new Passcode, or <see langword="null"/> to leave it unchanged.</param>
    /// <param name="startsAt">
    /// The Event's start to persist, or <see langword="null"/> to clear it - unlike <paramref name="name"/>/
    /// <paramref name="slug"/>/<paramref name="passcode"/>, there's no "leave unchanged" value: <c>startsAt</c>/
    /// <c>endsAt</c> always serialize on the wire (<see cref="EventAttributesDto.StartsAt"/>'s remarks), so a
    /// caller not changing this Event's start passes its current value back.
    /// </param>
    /// <param name="endsAt">The Event's end to persist, or <see langword="null"/> to clear it - see <paramref name="startsAt"/>.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="EventWriteOutcome.Success"/> (Api returns 204, no body to read back);
    /// <see cref="EventWriteOutcome.Forbidden"/> if the caller isn't an Admin;
    /// <see cref="EventWriteOutcome.Conflict"/> with the colliding attribute pointers; or
    /// <see cref="EventWriteOutcome.Invalid"/> with the offending pointer (ADR-0042).
    /// </returns>
    public async Task<(EventWriteOutcome Outcome, IReadOnlyList<string> ConflictPointers)> UpdateAsync(
        Guid id, string? name, string? slug, string? passcode,
        DateTimeOffset? startsAt, DateTimeOffset? endsAt, CancellationToken cancellationToken)
    {
        var body = new EventDocument
        {
            Data = new EventResourceObject
            {
                Type = ResourceType,
                Id = id.ToString(),
                Attributes = new EventAttributesDto
                {
                    Name = name,
                    Slug = slug,
                    Passcode = passcode,
                    StartsAt = startsAt,
                    EndsAt = endsAt
                }
            }
        };
        using var request = NewRequest(HttpMethod.Patch, $"{EventsPath}/{id}", body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (EventWriteOutcome.Forbidden, []);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (EventWriteOutcome.Conflict, await ReadErrorPointersAsync(response, cancellationToken));
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return (EventWriteOutcome.Invalid, await ReadErrorPointersAsync(response, cancellationToken));
        }

        EnsureExpectedStatus(response, HttpStatusCode.NoContent);
        return (EventWriteOutcome.Success, []);
    }

    /// <summary>
    /// Changes an Event's Status - the "Go live" and "Cancel event" actions, each its own dedicated PATCH sending
    /// only <c>status</c>, never folded into <see cref="UpdateAsync"/>'s general save.
    /// </summary>
    /// <param name="id">The Event whose Status is changing.</param>
    /// <param name="status">The target Status - only <see cref="EventStatus.Live"/> (from Draft) and <see cref="EventStatus.Cancelled"/> (from Live) are ever legal targets a caller sends.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="EventWriteOutcome.Success"/> (Api returns 204); <see cref="EventWriteOutcome.Forbidden"/> if
    /// the caller isn't an Admin; or <see cref="EventWriteOutcome.Invalid"/> if the transition is illegal
    /// (ADR-0044) - e.g. a concurrent change already moved this Event somewhere the requested transition can't
    /// start from.
    /// </returns>
    public async Task<EventWriteOutcome> SetStatusAsync(Guid id, EventStatus status, CancellationToken cancellationToken)
    {
        var body = new EventStatusDocument
        {
            Data = new EventStatusResourceObject
            {
                Type = ResourceType,
                Id = id.ToString(),
                Attributes = new EventStatusAttributesDto { Status = status }
            }
        };
        using var request = NewRequest(HttpMethod.Patch, $"{EventsPath}/{id}", body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return EventWriteOutcome.Forbidden;
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return EventWriteOutcome.Invalid;
        }

        EnsureExpectedStatus(response, HttpStatusCode.NoContent);
        return EventWriteOutcome.Success;
    }

    /// <summary>Deletes an Event permanently - hard delete, no recovery path (ADR-0045). Admin-only (ADR-0031).</summary>
    /// <param name="id">The Event to delete.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="EventWriteOutcome.Success"/> (Api returns 204); <see cref="EventWriteOutcome.Forbidden"/> if
    /// the caller isn't an Admin; or <see cref="EventWriteOutcome.NotFound"/> if the Event was already gone.
    /// </returns>
    public async Task<EventWriteOutcome> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Delete, $"{EventsPath}/{id}");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return EventWriteOutcome.Forbidden;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return EventWriteOutcome.NotFound;
        }

        EnsureExpectedStatus(response, HttpStatusCode.NoContent);
        return EventWriteOutcome.Success;
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri) => NewRequest<EventDocument>(method, uri, null);

    /// <remarks>
    /// Generic so <see cref="SetStatusAsync"/> can send its own minimal <see cref="EventStatusDocument"/>
    /// envelope through the same request-building path as every other write, without forcing it through
    /// <see cref="EventDocument"/>'s shape (see <see cref="EventStatusDocument"/>'s remarks for why that
    /// would be actively wrong here).
    /// </remarks>
    private static HttpRequestMessage NewRequest<TBody>(HttpMethod method, string uri, TBody? body)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        }

        return request;
    }

    /// <remarks>
    /// <see cref="InternalApiClient.SendAsync"/> doesn't itself wrap a transport failure on the call it
    /// makes (only <see cref="InternalJwtProvider"/>'s own grants lookup does, via
    /// <c>AuthorizationDataUnavailableException</c>) - that exception is let through unchanged since it
    /// already means "Api is unreachable"; anything else at the transport level becomes
    /// <see cref="EventDataUnavailableException"/> here, matching <c>ApiRoleGrantClient</c>'s discipline.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not AuthorizationDataUnavailableException && !cancellationToken.IsCancellationRequested)
        {
            throw new EventDataUnavailableException("The Event store (Api) is unreachable.", ex);
        }
    }

    private static void EnsureExpectedStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw new EventDataUnavailableException(
                $"The Event store (Api) returned an unexpected {(int)response.StatusCode} response.",
                new HttpRequestException(response.ReasonPhrase));
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken))!;

    private static async Task<IReadOnlyList<string>> ReadErrorPointersAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ErrorDocument document = await ReadAsync<ErrorDocument>(response, cancellationToken);
        return [.. document.Errors.Select(error => error.Source?.Pointer).OfType<string>()];
    }

    private static EventDto ToDto(EventResourceObject resource) => new()
    {
        Id = Guid.Parse(resource.Id!),
        Name = resource.Attributes!.Name!,
        Slug = resource.Attributes.Slug!,
        Passcode = resource.Attributes.Passcode!,
        Status = resource.Attributes.Status!.Value,
        StartsAt = resource.Attributes.StartsAt,
        EndsAt = resource.Attributes.EndsAt
    };

    /// <remarks>
    /// Omits <c>filter=</c> entirely for <see cref="EventStatusFilter.Current"/> - it's exactly what Api's own
    /// default collection view already applies (<c>EventResourceDefinition.OnApplyFilter</c>), so there's
    /// nothing to ask for. <see cref="EventStatusFilter.All"/> asks for every Status explicitly, since
    /// omitting the filter would ask for Current instead, not everything. Every value is PascalCase on the
    /// wire - Api's filter parser is case-sensitive (ADR-0053).
    /// </remarks>
    private static string BuildCollectionUri(int pageNumber, int pageSize, string? sort, EventStatusFilter status)
    {
        string uri = $"{EventsPath}?page[number]={pageNumber}&page[size]={pageSize}";

        if (!string.IsNullOrEmpty(sort))
        {
            uri += $"&sort={Uri.EscapeDataString(sort)}";
        }

        string? filter = status switch
        {
            EventStatusFilter.Current => null,
            EventStatusFilter.All => "any(status,'Draft','Live','Past','Cancelled')",
            EventStatusFilter.Draft => "equals(status,'Draft')",
            EventStatusFilter.Live => "equals(status,'Live')",
            EventStatusFilter.Past => "equals(status,'Past')",
            EventStatusFilter.Cancelled => "equals(status,'Cancelled')",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

        if (filter is not null)
        {
            uri += $"&filter={Uri.EscapeDataString(filter)}";
        }

        return uri;
    }
}
