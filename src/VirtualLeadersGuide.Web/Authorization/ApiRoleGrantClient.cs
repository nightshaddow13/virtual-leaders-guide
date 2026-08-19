using System.Net;
using System.Net.Http.Json;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Authorization;

/// <summary>Thin HTTP client over Api's internal authorization endpoints (see <see cref="InternalAuthorizationRoutes"/>).</summary>
/// <remarks>
/// Mirrors <c>ApiUserStore</c>'s shape - same <c>"Api"</c> <see cref="HttpClient"/>
/// (<c>InternalApiKeyHandler</c> + resilience already wired in <c>Program.cs</c>), same
/// <c>SendAsync</c>/<c>EnsureExpectedStatus</c> failure discipline.
/// </remarks>
public sealed class ApiRoleGrantClient(IHttpClientFactory httpClientFactory)
{
    public async Task<IReadOnlyList<RoleGrantDto>?> GetGrantsAsync(string userId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, InternalAuthorizationRoutes.ForUserGrants(userId));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<List<RoleGrantDto>>(cancellationToken);
    }

    /// <remarks>
    /// <see cref="GrantCreationOutcome.AlreadyGranted"/> (409) is an expected outcome for a caller
    /// re-syncing a grant that may already exist (e.g. P2-4's per-login allowlist sync), not an error -
    /// hence a typed outcome rather than an exception. <see cref="GrantCreationOutcome.UserOrRoleNotFound"/>
    /// (404) collapses two distinct failures the endpoint itself doesn't distinguish (see
    /// <c>InternalAuthorizationEndpoints.CreateGrantAsync</c>).
    /// </remarks>
    public async Task<(GrantCreationOutcome Outcome, RoleGrantDto? Grant)> CreateGrantAsync(
        string userId, int roleId, Guid? eventId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, InternalAuthorizationRoutes.ForUserGrants(userId))
        {
            Content = JsonContent.Create(new CreateRoleGrantRequest { RoleId = roleId, EventId = eventId })
        };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (GrantCreationOutcome.UserOrRoleNotFound, null);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (GrantCreationOutcome.AlreadyGranted, null);
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        RoleGrantDto? grant = await response.Content.ReadFromJsonAsync<RoleGrantDto>(cancellationToken);
        return (GrantCreationOutcome.Created, grant);
    }

    public async Task<bool> DeleteGrantAsync(string userId, Guid grantId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete, InternalAuthorizationRoutes.ForUserGrantById(userId, grantId));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        EnsureExpectedStatus(response, HttpStatusCode.NoContent, HttpStatusCode.NotFound);
        return response.StatusCode == HttpStatusCode.NoContent;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient("Api");

        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AuthorizationDataUnavailableException("The authorization store (Api) is unreachable.", ex);
        }
    }

    private static void EnsureExpectedStatus(HttpResponseMessage response, params ReadOnlySpan<HttpStatusCode> expected)
    {
        if (!expected.Contains(response.StatusCode))
        {
            throw new AuthorizationDataUnavailableException(
                $"The authorization store (Api) returned an unexpected {(int)response.StatusCode} response.",
                new HttpRequestException(response.ReasonPhrase));
        }
    }
}
