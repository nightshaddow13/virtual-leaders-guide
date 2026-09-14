using System.Net;
using System.Net.Http.Json;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.PublicGuide;

/// <summary>Thin HTTP client over Api's anonymous-reachable <c>/internal/public/*</c> surface (P4-2, #72).</summary>
/// <remarks>
/// Uses the bare <c>"Api"</c> named <see cref="HttpClient"/> directly, never
/// <see cref="Identity.InternalApiClient"/> - the established path for a call with no signed-in user (see
/// <c>Identity.ApiUserStore</c>/<c>Authorization.ApiRoleGrantClient</c>, and <c>InternalApiClient</c>'s own
/// remarks): an anonymous visitor's request carries only <c>X-Internal-Key</c>
/// (<see cref="InternalApiKeyHandler"/>), never an internal JWT, matching what
/// <c>PublicGuideEndpoints</c>' fallback-policy gate actually requires. Mirrors
/// <c>Events.ApiEventClient</c>'s discipline otherwise: typed outcomes for expected non-2xx responses, an
/// exception for anything else - but a plain-JSON request/response body, not JSON:API's envelope, since
/// <c>PublicGuideEndpoints</c> deliberately sits outside the <c>/api</c> namespace (same shape as
/// <c>Authorization.ApiRoleGrantClient</c>'s calls to <c>InternalAuthorizationEndpoints</c>).
/// </remarks>
public sealed class PublicEventClient(IHttpClientFactory httpClientFactory)
{
    /// <summary>Looks up an Event by its public Slug.</summary>
    /// <param name="slug">The Slug from a visitor's typed event address (already normalized by the caller).</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="PublicEventLookupOutcome.Success"/> with the Event, or
    /// <see cref="PublicEventLookupOutcome.NotFound"/> for an unknown Slug or a <c>Draft</c> Event -
    /// indistinguishable on purpose (<c>PublicGuideEndpoints</c>'s remarks).
    /// </returns>
    public async Task<(PublicEventLookupOutcome Outcome, PublicEventDto? Event)> GetBySlugAsync(
        string slug, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Get, PublicGuideRoutes.ForEventBySlug(slug), null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (PublicEventLookupOutcome.NotFound, null);
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        return (PublicEventLookupOutcome.Success,
            await response.Content.ReadFromJsonAsync<PublicEventDto>(cancellationToken));
    }

    /// <summary>Checks a visitor's typed Passcode guess against an Event's real one.</summary>
    /// <param name="slug">The Event's Slug.</param>
    /// <param name="passcode">The visitor's typed guess, exactly as entered.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="PasscodeCheckOutcome.Matched"/> with the Event id and its current
    /// <c>PasscodeVersion</c> (to stamp into the Unlock cookie, ADR-0057);
    /// <see cref="PasscodeCheckOutcome.NotMatched"/> for a wrong guess; or
    /// <see cref="PasscodeCheckOutcome.EventNotFound"/> for an unknown Slug or a <c>Draft</c> Event.
    /// </returns>
    public async Task<(PasscodeCheckOutcome Outcome, Guid? EventId, int? PasscodeVersion)> CheckPasscodeAsync(
        string slug, string passcode, CancellationToken cancellationToken)
    {
        var body = new PasscodeCheckRequest { Passcode = passcode };
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Post, PublicGuideRoutes.ForPasscodeCheck(slug), body, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (PasscodeCheckOutcome.EventNotFound, null, null);
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        PasscodeCheckResult result = (await response.Content.ReadFromJsonAsync<PasscodeCheckResult>(cancellationToken))!;

        return result.Matched
            ? (PasscodeCheckOutcome.Matched, result.EventId, result.PasscodeVersion)
            : (PasscodeCheckOutcome.NotMatched, null, null);
    }

    /// <remarks>
    /// Mirrors <c>Events.ApiEventClient.SendAsync</c>'s discipline: any transport-level failure becomes
    /// <see cref="PublicGuideUnavailableException"/> rather than propagating raw, so a caller fails loudly
    /// through one exception type instead of catching <see cref="HttpRequestException"/> directly.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            HttpClient client = httpClientFactory.CreateClient("Api");
            return await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PublicGuideUnavailableException("The public guide lookup (Api) is unreachable.", ex);
        }
    }

    private static void EnsureExpectedStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw new PublicGuideUnavailableException(
                $"The public guide lookup (Api) returned an unexpected {(int)response.StatusCode} response.",
                new HttpRequestException(response.ReasonPhrase));
        }
    }
}
