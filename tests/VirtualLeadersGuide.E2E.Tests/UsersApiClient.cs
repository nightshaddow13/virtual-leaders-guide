using System.Net.Http.Json;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Lists local-Identity accounts by email directly against <c>Api</c>'s <c>/api/users</c> JSON:API resource -
/// for <see cref="AspireE2EFixture"/>'s run-end sweep (ADR-0039), which needs to enumerate every
/// <c>@example.test</c> account rather than look one up by a known email
/// (<see cref="IdentityApiClient.TryGetByEmailAsync"/> already covers that case).
/// </summary>
/// <remarks>
/// Unlike <see cref="IdentityApiClient"/>, <c>/api/users</c> sits behind
/// <see cref="InternalJwtDefaults.PolicyName"/> and is Admin-only (<c>ApplicationUserAccessPolicy.CanRead</c>)
/// - see <see cref="EventsApiClient"/>'s own remarks for the same shape; this class shares its
/// <see cref="AdminJsonApiClientBase"/> request plumbing and <see cref="InternalAdminJwt"/> minting recipe.
/// </remarks>
public sealed class UsersApiClient(HttpClient httpClient, string internalJwtSigningKey)
    : AdminJsonApiClientBase(httpClient, internalJwtSigningKey)
{
    private const string UsersPath = "/api/users";

    /// <summary>
    /// Every User's email ending in <c>@example.test</c> - the reserved TLD (RFC 6761) every account this
    /// suite creates uses, and the only domain the run-end sweep is ever allowed to touch (ADR-0039).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Every matching email, across all pages.</returns>
    /// <exception cref="InvalidOperationException">The list request did not succeed.</exception>
    public async Task<IReadOnlyList<string>> ListExampleTestEmailsAsync(CancellationToken cancellationToken)
    {
        string uri = BuildUnpagedCollectionUri(UsersPath, "endsWith(email,'@example.test')");
        using HttpRequestMessage request = NewRequest(HttpMethod.Get, uri);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync("list", "@example.test Users", response, cancellationToken);
        }

        UserCollectionDocument document =
            (await response.Content.ReadFromJsonAsync<UserCollectionDocument>(JsonOptions, cancellationToken))!;
        return [.. document.Data.Select(resource => resource.Attributes!.Email!)];
    }

    private sealed class UserCollectionDocument
    {
        public required List<UserResourceObject> Data { get; init; }
    }

    private sealed class UserResourceObject
    {
        public UserAttributesDto? Attributes { get; init; }
    }

    private sealed class UserAttributesDto
    {
        public string? Email { get; init; }
    }
}
