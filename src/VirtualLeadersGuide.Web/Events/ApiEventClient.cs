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
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// The page of Events, and the total count of Events visible to the caller across all pages - never
    /// forbidden: an Admin sees every Event, a Director's collection is silently narrowed to their assigned
    /// Events (possibly empty) rather than denied (ADR-0031).
    /// </returns>
    public async Task<(IReadOnlyList<EventDto> Events, int Total)> GetEventsAsync(
        int pageNumber, int pageSize, string? sort, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, BuildCollectionUri(pageNumber, pageSize, sort));
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

    /// <summary>Creates a new Event with only a Name - Api derives the Slug and generates the Passcode.</summary>
    /// <param name="name">The new Event's display name.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="EventWriteOutcome.Success"/> with the created Event (its server-derived Slug and
    /// generated Passcode included); <see cref="EventWriteOutcome.Forbidden"/> if the caller isn't an Admin
    /// (ADR-0031); or <see cref="EventWriteOutcome.Conflict"/> with the colliding attribute pointers if
    /// <paramref name="name"/> or its derived Slug is already in use.
    /// </returns>
    public async Task<(EventWriteOutcome Outcome, EventDto? Event, IReadOnlyList<string> ConflictPointers)> CreateAsync(
        string name, CancellationToken cancellationToken)
    {
        var body = new EventDocument
        {
            Data = new EventResourceObject { Type = ResourceType, Attributes = new EventAttributesDto { Name = name } }
        };
        using var request = NewRequest(HttpMethod.Post, EventsPath, body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (EventWriteOutcome.Forbidden, null, []);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (EventWriteOutcome.Conflict, null, await ReadConflictPointersAsync(response, cancellationToken));
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
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="EventWriteOutcome.Success"/> (Api returns 204, no body to read back);
    /// <see cref="EventWriteOutcome.Forbidden"/> if the caller isn't an Admin; or
    /// <see cref="EventWriteOutcome.Conflict"/> with the colliding attribute pointers.
    /// </returns>
    public async Task<(EventWriteOutcome Outcome, IReadOnlyList<string> ConflictPointers)> UpdateAsync(
        Guid id, string? name, string? slug, string? passcode, CancellationToken cancellationToken)
    {
        var body = new EventDocument
        {
            Data = new EventResourceObject
            {
                Type = ResourceType,
                Id = id.ToString(),
                Attributes = new EventAttributesDto { Name = name, Slug = slug, Passcode = passcode }
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
            return (EventWriteOutcome.Conflict, await ReadConflictPointersAsync(response, cancellationToken));
        }

        EnsureExpectedStatus(response, HttpStatusCode.NoContent);
        return (EventWriteOutcome.Success, []);
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri, EventDocument? body = null)
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

    private static async Task<IReadOnlyList<string>> ReadConflictPointersAsync(
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
        Passcode = resource.Attributes.Passcode!
    };

    private static string BuildCollectionUri(int pageNumber, int pageSize, string? sort)
    {
        string uri = $"{EventsPath}?page[number]={pageNumber}&page[size]={pageSize}";
        return string.IsNullOrEmpty(sort) ? uri : $"{uri}&sort={Uri.EscapeDataString(sort)}";
    }
}
