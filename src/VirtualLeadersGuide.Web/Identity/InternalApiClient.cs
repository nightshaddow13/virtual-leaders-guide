using System.Net.Http.Headers;
using VirtualLeadersGuide.Web.Authorization;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Attaches the current circuit's internal JWT (<see cref="InternalJwtProvider"/>) as a bearer token to
/// requests sent through the <c>"Api"</c> named <see cref="HttpClient"/>, for calls made on behalf of a
/// signed-in user (P2-5, #14; ADR-0007). <c>X-Internal-Key</c> is still attached underneath by
/// <c>InternalApiKeyHandler</c>, unchanged - this adds the second, per-user proof alongside it.
/// </summary>
/// <remarks>
/// Deliberately a scoped wrapper service, not a second <see cref="DelegatingHandler"/> registered next to
/// <c>InternalApiKeyHandler</c> via <c>AddHttpMessageHandler</c> - even though that would look more
/// symmetric. <see cref="IHttpClientFactory"/> builds and pools each named client's handler chain from its
/// own internal DI scope, not the caller's request/circuit scope. <see cref="InternalJwtProvider"/> must be
/// scoped per Blazor circuit (ADR-0007's caching unit), so a <see cref="DelegatingHandler"/> capturing it via
/// constructor injection would bind to whatever scope the factory happened to build the handler chain in -
/// not reliably the circuit actually calling <see cref="SendAsync"/> - and that binding could then be reused
/// across circuits once the pooled handler rotates. Resolving <see cref="InternalJwtProvider"/> here, from
/// this service's own (correctly-scoped) constructor, sidesteps that mismatch entirely rather than working
/// around it.
/// <para>
/// <see cref="ApiUserStore"/> and <see cref="ApiRoleGrantClient"/> deliberately keep using the bare
/// <c>"Api"</c> client directly rather than this type - both run on paths where no user token exists yet
/// (backing sign-in itself, or minting the token this type attaches).
/// </para>
/// </remarks>
public sealed class InternalApiClient(IHttpClientFactory httpClientFactory, InternalJwtProvider jwtProvider)
{
    /// <summary>
    /// Sends <paramref name="request"/> to <c>Api</c> with the current circuit's internal JWT attached as a
    /// bearer token.
    /// </summary>
    /// <param name="request">The request to send. Its <see cref="HttpRequestMessage.RequestUri"/> is treated
    /// as relative to the <c>"Api"</c> client's base address, matching <c>ApiUserStore</c>'s usage.</param>
    /// <param name="cancellationToken">Propagated to both the token mint (if needed) and the HTTP call.</param>
    /// <returns>The raw response from <c>Api</c>.</returns>
    /// <exception cref="AuthorizationDataUnavailableException">
    /// Propagated from <see cref="InternalJwtProvider.GetTokenAsync"/> if a fresh token needs minting and
    /// <c>Api</c>'s role-grants lookup is unreachable.
    /// </exception>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string token = await jwtProvider.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpClient client = httpClientFactory.CreateClient("Api");
        return await client.SendAsync(request, cancellationToken);
    }
}
