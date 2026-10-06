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
/// <c>/api/*</c> requires the internal JWT that only <see cref="InternalApiClient"/> attaches. Create-only
/// for now - P5-7/P5-8/P5-9 (#93/#94/#95) add the read/update/delete methods <c>ActivityResourceDefinition</c>
/// already authorizes on the Api side, once their own UI needs to call them.
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

    private static HttpRequestMessage NewRequest<TBody>(HttpMethod method, string uri, TBody body)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));
        request.Content = JsonContent.Create(body, options: JsonOptions);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
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
}
