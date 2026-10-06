using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>
/// Shared HTTP request plumbing for <see cref="ApiFacilityClient"/> and <see cref="ApiFacilityTypeClient"/>
/// (P8-2, #166) - the two Web API clients talking to Api's <c>/api/facilities</c>/<c>/api/facilityTypes</c>
/// resources, which otherwise carried near-identical copies of this class's members.
/// </summary>
/// <remarks>
/// Mirrors <c>E2E.Tests.AdminJsonApiClientBase</c>'s role for its own sibling pair
/// (<c>EventsApiClient</c>/<c>UsersApiClient</c>), applied here for the first time on the Web layer - every
/// other feature area (<c>Activities</c>, <c>InfoPages</c>, <c>Events</c>) has exactly one client, so there
/// was never a sibling to share this plumbing with before Facility.
/// </remarks>
public abstract class FacilityApiClientBase(InternalApiClient apiClient, string storeName)
{
    protected const string JsonApiMediaType = "application/vnd.api+json";

    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Builds a bodyless request (e.g. a GET) carrying the JSON:API <c>Accept</c> header.</summary>
    protected static HttpRequestMessage NewRequest(HttpMethod method, string uri) => NewRequest<object>(method, uri, null);

    /// <summary>Builds a request carrying the JSON:API <c>Accept</c>/<c>Content-Type</c> headers and, when <paramref name="body"/> isn't <see langword="null"/>, a serialized JSON:API body.</summary>
    protected static HttpRequestMessage NewRequest<TBody>(HttpMethod method, string uri, TBody? body)
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
    /// at the transport level becomes <see cref="FacilityDataUnavailableException"/> here, naming
    /// <paramref name="storeName"/> in the message the same way every other <c>Api*Client</c> in this app does.
    /// </remarks>
    protected async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not AuthorizationDataUnavailableException && !cancellationToken.IsCancellationRequested)
        {
            throw new FacilityDataUnavailableException($"The {storeName} store (Api) is unreachable.", ex);
        }
    }

    protected void EnsureExpectedStatus(HttpResponseMessage response, params ReadOnlySpan<HttpStatusCode> expected)
    {
        if (!expected.Contains(response.StatusCode))
        {
            throw new FacilityDataUnavailableException(
                $"The {storeName} store (Api) returned an unexpected {(int)response.StatusCode} response.",
                new HttpRequestException(response.ReasonPhrase));
        }
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken))!;
}
