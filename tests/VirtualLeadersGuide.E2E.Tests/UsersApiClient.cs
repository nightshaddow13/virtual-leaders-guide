using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
/// <see cref="InternalAdminJwt"/> minting recipe.
/// </remarks>
public sealed class UsersApiClient(HttpClient httpClient, string internalJwtSigningKey)
{
    private const string UsersPath = "/api/users";
    private const string JsonApiMediaType = "application/vnd.api+json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Every User's email ending in <c>@example.test</c> - the reserved TLD (RFC 6761) every account this
    /// suite creates uses, and the only domain the run-end sweep is ever allowed to touch (ADR-0039).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The list request did not succeed.</exception>
    public async Task<IReadOnlyList<string>> ListExampleTestEmailsAsync(CancellationToken cancellationToken)
    {
        string uri = $"{UsersPath}?filter=endsWith(email,'@example.test')&page[size]=9999";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", InternalAdminJwt.Mint(internalJwtSigningKey));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                "UsersApiClient failed to list @example.test Users: " +
                $"{(int)response.StatusCode} {response.StatusCode}. {body}");
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
