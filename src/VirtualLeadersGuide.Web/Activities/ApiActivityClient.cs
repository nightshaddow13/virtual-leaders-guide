using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;
using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Activities;

/// <summary>Thin HTTP client over Api's <c>/api/activities</c> JSON:API resource (P5-6, #87).</summary>
/// <remarks>
/// Mirrors <c>InfoPages.ApiInfoPageClient</c>'s shape - typed outcomes for expected non-2xx responses rather
/// than exceptions, <see cref="ActivityDataUnavailableException"/> for everything else. Requests go through
/// <see cref="InternalApiClient"/>, not a bare <c>IHttpClientFactory.CreateClient("Api")</c>, because
/// <c>/api/*</c> requires the internal JWT that only <see cref="InternalApiClient"/> attaches. The
/// read/update/delete methods <c>ActivityResourceDefinition</c> already authorizes on the Api side arrive as
/// their own UI needs them - P5-7 (#93) adds the list read, P5-8 (#94) the single read and update; P5-9
/// (#95) still owes delete.
/// </remarks>
public sealed class ApiActivityClient(InternalApiClient apiClient)
{
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const string ActivitiesPath = "/api/activities";
    private const string ResourceType = "activities";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Lists one Event's Activities, sorted by Name ascending unless <paramref name="sort"/> says otherwise.</summary>
    /// <param name="eventId">The Event whose Activities to list.</param>
    /// <param name="pageNumber">The 1-based page to fetch.</param>
    /// <param name="pageSize">The number of Activities per page.</param>
    /// <param name="sort">
    /// A JSON:API <c>sort=</c> value (e.g. <c>name</c> or <c>-name</c>, see <c>JsonApi.JsonApiSort</c>), or
    /// <see langword="null"/> to fall back to <c>name</c> ascending - the wireframe's default order.
    /// </param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// The page of Activities, and the total count across all pages. Never forbidden: an unassigned
    /// Director's request is silently narrowed to nothing by Api rather than denied (ADR-0069) - callers
    /// needing to distinguish "no Activities yet" from "not assigned to this Event" gate on
    /// <see cref="ApiEventClient"/>'s own read first, not on this call.
    /// </returns>
    public async Task<(IReadOnlyList<ActivityDto> Activities, int Total)> GetActivitiesForEventAsync(
        Guid eventId, int pageNumber, int pageSize, string? sort, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, BuildCollectionUri(eventId, pageNumber, pageSize, sort));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        ActivityCollectionDocument document = await ReadAsync<ActivityCollectionDocument>(response, cancellationToken);
        var activities = document.Data.Select(ToDto).ToList();
        return (activities, document.Meta?.Total ?? activities.Count);
    }

    /// <summary>Reads a single Activity by id.</summary>
    /// <param name="id">The Activity's id.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="ActivityReadOutcome.Success"/> with the Activity; <see cref="ActivityReadOutcome.Forbidden"/>
    /// if the caller can't read it (an unassigned Director, or the Activity doesn't exist for a non-Admin,
    /// ADR-0069); or <see cref="ActivityReadOutcome.NotFound"/> if it doesn't exist for an Admin.
    /// </returns>
    public async Task<(ActivityReadOutcome Outcome, ActivityDto? Activity)> GetActivityAsync(
        Guid id, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, $"{ActivitiesPath}/{id}");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (ActivityReadOutcome.Forbidden, null);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (ActivityReadOutcome.NotFound, null);
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        ActivityDocument document = await ReadAsync<ActivityDocument>(response, cancellationToken);
        return (ActivityReadOutcome.Success, ToDto(document.Data));
    }

    /// <summary>Creates a new Activity on the given Event.</summary>
    /// <param name="eventId">The Event this Activity belongs to.</param>
    /// <param name="name">The Activity's display Name.</param>
    /// <param name="description">The Activity's rich text description, as raw markdown - the empty string is legal.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="ActivityWriteOutcome.Success"/> with the created Activity;
    /// <see cref="ActivityWriteOutcome.Forbidden"/> if the caller isn't an Admin or an assigned Director
    /// (ADR-0069); or <see cref="ActivityWriteOutcome.Invalid"/> with the offending pointer if
    /// <paramref name="eventId"/> doesn't exist.
    /// </returns>
    public async Task<(ActivityWriteOutcome Outcome, ActivityDto? Activity, IReadOnlyList<string> Pointers)> CreateAsync(
        Guid eventId, string name, string description, CancellationToken cancellationToken)
    {
        var body = new ActivityDocument
        {
            Data = new ActivityResourceObject
            {
                Type = ResourceType,
                Attributes = new ActivityAttributesDto { EventId = eventId, Name = name, Description = description }
            }
        };
        using var request = NewRequest(HttpMethod.Post, ActivitiesPath, body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (ActivityWriteOutcome.Forbidden, null, []);
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return (ActivityWriteOutcome.Invalid, null, await ReadErrorPointersAsync(response, cancellationToken));
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        ActivityDocument created = await ReadAsync<ActivityDocument>(response, cancellationToken);
        return (ActivityWriteOutcome.Success, ToDto(created.Data), []);
    }

    /// <summary>Updates an existing Activity's Name and Description.</summary>
    /// <param name="id">The Activity to update.</param>
    /// <param name="name">The new Name.</param>
    /// <param name="description">The new rich text description, as raw markdown - the empty string is legal.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="ActivityWriteOutcome.Success"/> (Api returns 204); or <see cref="ActivityWriteOutcome.Forbidden"/>
    /// if the caller no longer has write access (ADR-0069).
    /// </returns>
    /// <remarks>
    /// Never sends <c>eventId</c>: <c>Activity.EventId</c> carries no <c>AllowChange</c>, so including it would
    /// be a 422 - moving an Activity between Events isn't a thing this app does.
    /// </remarks>
    public async Task<ActivityWriteOutcome> UpdateAsync(
        Guid id, string name, string description, CancellationToken cancellationToken)
    {
        var body = new ActivityDocument
        {
            Data = new ActivityResourceObject
            {
                Type = ResourceType,
                Id = id.ToString(),
                Attributes = new ActivityAttributesDto { Name = name, Description = description }
            }
        };
        using var request = NewRequest(HttpMethod.Patch, $"{ActivitiesPath}/{id}", body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return ActivityWriteOutcome.Forbidden;
        }

        EnsureExpectedStatus(response, HttpStatusCode.NoContent);
        return ActivityWriteOutcome.Success;
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri) => NewRequest<ActivityDocument>(method, uri, null);

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
    /// <see cref="InternalApiClient.SendAsync"/> doesn't itself wrap a transport failure (only
    /// <see cref="InternalJwtProvider"/>'s own grants lookup does, via <c>AuthorizationDataUnavailableException</c>)
    /// - that exception is let through unchanged since it already means "Api is unreachable"; anything else
    /// at the transport level becomes <see cref="ActivityDataUnavailableException"/> here, matching
    /// <c>ApiInfoPageClient</c>'s discipline.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not AuthorizationDataUnavailableException && !cancellationToken.IsCancellationRequested)
        {
            throw new ActivityDataUnavailableException("The Activity store (Api) is unreachable.", ex);
        }
    }

    private static void EnsureExpectedStatus(HttpResponseMessage response, params ReadOnlySpan<HttpStatusCode> expected)
    {
        if (!expected.Contains(response.StatusCode))
        {
            throw new ActivityDataUnavailableException(
                $"The Activity store (Api) returned an unexpected {(int)response.StatusCode} response.",
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

    private static ActivityDto ToDto(ActivityResourceObject resource) => new()
    {
        Id = Guid.Parse(resource.Id!),
        EventId = resource.Attributes!.EventId!.Value,
        Name = resource.Attributes.Name!,
        Description = resource.Attributes.Description!
    };

    /// <remarks>
    /// Defaults to <c>name</c> ascending when <paramref name="sort"/> is <see langword="null"/> - the
    /// wireframe's drawn order (turn 1a). Guid values are interpolated unescaped inside single quotes,
    /// matching <c>ApiInfoPageClient.BuildCollectionUri</c>'s <c>equals(field,'guid')</c> filter-building
    /// precedent - a Guid's string form contains no characters JSON:API's filter grammar would misparse.
    /// </remarks>
    private static string BuildCollectionUri(Guid eventId, int pageNumber, int pageSize, string? sort)
    {
        string filter = Uri.EscapeDataString($"equals(eventId,'{eventId}')");
        return $"{ActivitiesPath}?filter={filter}&sort={sort ?? "name"}&page[number]={pageNumber}&page[size]={pageSize}";
    }
}
