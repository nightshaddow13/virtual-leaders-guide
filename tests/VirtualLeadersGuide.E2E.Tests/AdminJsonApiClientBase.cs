using System.Net.Http.Headers;
using System.Text.Json;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Shared request plumbing for <see cref="EventsApiClient"/> and <see cref="UsersApiClient"/> - the two
/// test-side clients that call Admin-gated <c>/api/*</c> JSON:API resources directly, each minting its own
/// bearer token per request via <see cref="InternalAdminJwt"/> (ADR-0039). See either derived class's own
/// remarks for why <c>/api/*</c> needs a bearer token at all, unlike <see cref="IdentityApiClient"/>'s plain
/// <c>X-Internal-Key</c> channel over <c>/internal/*</c>.
/// </summary>
public abstract class AdminJsonApiClientBase(HttpClient httpClient, string internalJwtSigningKey)
{
    protected const string JsonApiMediaType = "application/vnd.api+json";

    /// <summary>Base <c>System.Text.Json</c> defaults for reading a JSON:API response - camelCase property names.</summary>
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Builds a JSON:API collection URI with a filter and no real pagination - both derived clients only ever need "every match," never a specific page.</summary>
    /// <param name="path">The resource path (e.g. <c>/api/events</c>).</param>
    /// <param name="filter">A JSON:API filter expression, unescaped (e.g. <c>startsWith(name,'e2e-')</c>).</param>
    /// <returns>The full relative URI, including a <c>page[size]</c> large enough to return every match in one page.</returns>
    protected static string BuildUnpagedCollectionUri(string path, string filter) =>
        $"{path}?filter={filter}&page[size]=9999";

    /// <summary>Builds a request carrying the JSON:API <c>Accept</c> header and a fresh Admin bearer token.</summary>
    /// <param name="method">The HTTP method to use.</param>
    /// <param name="uri">The relative URI to request.</param>
    /// <returns>The request, ready to send or to attach a body to.</returns>
    protected HttpRequestMessage NewRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", InternalAdminJwt.Mint(internalJwtSigningKey));
        return request;
    }

    /// <summary>Sends <paramref name="request"/> over the shared <see cref="HttpClient"/> both derived clients were constructed with.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The response, still open - the caller disposes it.</returns>
    protected Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        httpClient.SendAsync(request, cancellationToken);

    /// <summary>Throws with a message identifying the concrete client, action, subject, and response - the shared failure shape every call site reports through.</summary>
    /// <param name="action">A short verb phrase describing what failed (e.g. <c>"create"</c>, <c>"list"</c>).</param>
    /// <param name="subject">The thing the action was performed on (e.g. an id or name), for the message.</param>
    /// <param name="response">The failed response - its body is read and included.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Always - this method's entire purpose is to throw one.</exception>
    protected async Task ThrowForFailureAsync(
        string action, string subject, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"{GetType().Name} failed to {action} '{subject}': {(int)response.StatusCode} " +
            $"{response.StatusCode}. {body}");
    }
}
