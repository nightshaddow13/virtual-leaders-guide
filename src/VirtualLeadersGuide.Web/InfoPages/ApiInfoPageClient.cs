using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;
using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>Thin HTTP client over Api's <c>/api/infoPages</c> JSON:API resource (P5-16, #21).</summary>
/// <remarks>
/// Mirrors <c>Events.ApiEventClient</c>'s shape - typed outcomes for expected non-2xx responses rather than
/// exceptions, <see cref="InfoPageDataUnavailableException"/> for everything else. Requests go through
/// <see cref="InternalApiClient"/>, not a bare <c>IHttpClientFactory.CreateClient("Api")</c>, because
/// <c>/api/*</c> requires the internal JWT that only <see cref="InternalApiClient"/> attaches.
/// </remarks>
public sealed class ApiInfoPageClient(InternalApiClient apiClient)
{
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const string InfoPagesPath = "/api/infoPages";
    private const string ResourceType = "infoPages";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Lists one Event's InfoPages, sorted by Title ascending (<c>Page</c> has no ordering column - P5-15, #20).</summary>
    /// <param name="eventId">The Event whose InfoPages to list.</param>
    /// <param name="pageNumber">The 1-based page to fetch.</param>
    /// <param name="pageSize">The number of InfoPages per page.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// The page of InfoPages, and the total count across all pages. Never forbidden: an unassigned Director's
    /// request is silently narrowed to nothing by Api rather than denied (ADR-0059) - callers needing to
    /// distinguish "no InfoPages yet" from "not assigned to this Event" gate on <see cref="ApiEventClient"/>'s
    /// own read first, not on this call.
    /// </returns>
    public async Task<(IReadOnlyList<InfoPageDto> InfoPages, int Total)> GetInfoPagesForEventAsync(
        Guid eventId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, BuildCollectionUri(eventId, pageNumber, pageSize));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        InfoPageCollectionDocument document = await ReadAsync<InfoPageCollectionDocument>(response, cancellationToken);
        var infoPages = document.Data.Select(ToDto).ToList();
        return (infoPages, document.Meta?.Total ?? infoPages.Count);
    }

    /// <summary>Reads a single InfoPage by id.</summary>
    /// <param name="id">The InfoPage's id.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="InfoPageReadOutcome.Success"/> with the InfoPage; <see cref="InfoPageReadOutcome.Forbidden"/>
    /// if the caller can't read it (an unassigned Director, or the InfoPage doesn't exist for a non-Admin,
    /// ADR-0059); or <see cref="InfoPageReadOutcome.NotFound"/> if it doesn't exist for an Admin.
    /// </returns>
    public async Task<(InfoPageReadOutcome Outcome, InfoPageDto? InfoPage)> GetInfoPageAsync(
        Guid id, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, $"{InfoPagesPath}/{id}");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (InfoPageReadOutcome.Forbidden, null);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (InfoPageReadOutcome.NotFound, null);
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        InfoPageDocument document = await ReadAsync<InfoPageDocument>(response, cancellationToken);
        return (InfoPageReadOutcome.Success, ToDto(document.Data));
    }

    /// <summary>Creates a new InfoPage on the given Event.</summary>
    /// <param name="eventId">The Event this InfoPage belongs to.</param>
    /// <param name="title">The InfoPage's display Title - not required to be unique (CONTEXT.md's InfoPage entry).</param>
    /// <param name="markdownContent">The InfoPage's raw markdown content - the empty string is legal.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="InfoPageWriteOutcome.Success"/> with the created InfoPage;
    /// <see cref="InfoPageWriteOutcome.Forbidden"/> if the caller isn't an Admin or an assigned Director
    /// (ADR-0059); or <see cref="InfoPageWriteOutcome.Invalid"/> with the offending pointer if
    /// <paramref name="eventId"/> doesn't exist.
    /// </returns>
    public async Task<(InfoPageWriteOutcome Outcome, InfoPageDto? InfoPage, IReadOnlyList<string> Pointers)> CreateAsync(
        Guid eventId, string title, string markdownContent, CancellationToken cancellationToken)
    {
        var body = new InfoPageDocument
        {
            Data = new InfoPageResourceObject
            {
                Type = ResourceType,
                Attributes = new InfoPageAttributesDto { EventId = eventId, Title = title, MarkdownContent = markdownContent }
            }
        };
        using var request = NewRequest(HttpMethod.Post, InfoPagesPath, body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (InfoPageWriteOutcome.Forbidden, null, []);
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return (InfoPageWriteOutcome.Invalid, null, await ReadErrorPointersAsync(response, cancellationToken));
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        InfoPageDocument created = await ReadAsync<InfoPageDocument>(response, cancellationToken);
        return (InfoPageWriteOutcome.Success, ToDto(created.Data), []);
    }

    /// <summary>Updates an existing InfoPage's Title and/or markdown content.</summary>
    /// <param name="id">The InfoPage to update.</param>
    /// <param name="title">The new Title.</param>
    /// <param name="markdownContent">The new markdown content - the empty string is legal.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="InfoPageWriteOutcome.Success"/> (Api returns 204 - unlike Event, InfoPage has no
    /// computed field that would make Api return the resource instead); or
    /// <see cref="InfoPageWriteOutcome.Forbidden"/> if the caller no longer has write access.
    /// </returns>
    public async Task<InfoPageWriteOutcome> UpdateAsync(
        Guid id, string title, string markdownContent, CancellationToken cancellationToken)
    {
        var body = new InfoPageDocument
        {
            Data = new InfoPageResourceObject
            {
                Type = ResourceType,
                Id = id.ToString(),
                Attributes = new InfoPageAttributesDto { Title = title, MarkdownContent = markdownContent }
            }
        };
        using var request = NewRequest(HttpMethod.Patch, $"{InfoPagesPath}/{id}", body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return InfoPageWriteOutcome.Forbidden;
        }

        EnsureExpectedStatus(response, HttpStatusCode.NoContent);
        return InfoPageWriteOutcome.Success;
    }

    /// <summary>Deletes an InfoPage permanently - hard delete, no recovery path (ADR-0045).</summary>
    /// <param name="id">The InfoPage to delete.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="InfoPageWriteOutcome.Success"/> (Api returns 204); <see cref="InfoPageWriteOutcome.Forbidden"/>
    /// if the caller no longer has write access; or <see cref="InfoPageWriteOutcome.NotFound"/> if it was
    /// already gone.
    /// </returns>
    public async Task<InfoPageWriteOutcome> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Delete, $"{InfoPagesPath}/{id}");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return InfoPageWriteOutcome.Forbidden;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return InfoPageWriteOutcome.NotFound;
        }

        EnsureExpectedStatus(response, HttpStatusCode.NoContent);
        return InfoPageWriteOutcome.Success;
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri) => NewRequest<InfoPageDocument>(method, uri, null);

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
    /// at the transport level becomes <see cref="InfoPageDataUnavailableException"/> here, matching
    /// <c>ApiEventClient</c>'s discipline.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not AuthorizationDataUnavailableException && !cancellationToken.IsCancellationRequested)
        {
            throw new InfoPageDataUnavailableException("The InfoPage store (Api) is unreachable.", ex);
        }
    }

    private static void EnsureExpectedStatus(HttpResponseMessage response, params ReadOnlySpan<HttpStatusCode> expected)
    {
        if (!expected.Contains(response.StatusCode))
        {
            throw new InfoPageDataUnavailableException(
                $"The InfoPage store (Api) returned an unexpected {(int)response.StatusCode} response.",
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

    private static InfoPageDto ToDto(InfoPageResourceObject resource) => new()
    {
        Id = Guid.Parse(resource.Id!),
        EventId = resource.Attributes!.EventId!.Value,
        Title = resource.Attributes.Title!,
        MarkdownContent = resource.Attributes.MarkdownContent!
    };

    /// <remarks>
    /// Always sorts by <c>title</c> - <c>Page</c> has no <c>SortOrder</c>/<c>CreatedAt</c> column to order
    /// by instead (P5-15, #20's correction comment on this ticket). Guid values are interpolated unescaped
    /// inside single quotes, matching <c>ApiDirectorClient</c>'s <c>equals(field,'guid')</c> filter-building
    /// precedent - a Guid's string form contains no characters JSON:API's filter grammar would misparse.
    /// </remarks>
    private static string BuildCollectionUri(Guid eventId, int pageNumber, int pageSize)
    {
        string filter = Uri.EscapeDataString($"equals(eventId,'{eventId}')");
        return $"{InfoPagesPath}?filter={filter}&sort=title&page[number]={pageNumber}&page[size]={pageSize}";
    }
}
