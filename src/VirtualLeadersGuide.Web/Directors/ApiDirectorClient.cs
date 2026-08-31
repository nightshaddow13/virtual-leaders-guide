using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// Thin HTTP client over Api's <c>/api/users</c> and <c>/api/roleGrants</c> JSON:API resources, joined
/// client-side into <see cref="UserRowDto"/> rows for the P2-12 (#43) Users screen and the EventEditor
/// Directors section.
/// </summary>
/// <remarks>
/// Mirrors <c>Events.ApiEventClient</c>'s shape - typed outcomes for expected non-2xx responses,
/// <see cref="DirectorDataUnavailableException"/> for everything else, and requests go through
/// <see cref="InternalApiClient"/> (not the bare <c>"Api"</c> client <c>ApiRoleGrantClient</c> uses for
/// <c>/internal/authorization</c>) so <c>/api/roleGrants</c>' and <c>/api/users</c>' Admin-only gates
/// (ADR-0033, and this ticket's own gate on <c>/api/users</c>) actually apply - the point of routing through
/// this resource instead of the internal endpoint is server-side enforcement, not just a Blazor-side check.
/// <para>
/// <c>UserRole.User</c> isn't a JSON:API relationship (ADR-0024), so there's no server-side
/// <c>?include=</c> to fetch a pre-joined view - every method here fetches from both resources and joins in
/// memory. <see cref="GetUsersAsync"/> fetches every User and every Grant in one page each rather than
/// pushing search/state filtering to Api, which keeps this join simple and avoids relying on
/// <c>ApplicationUser.HasCredential</c> (a computed, non-column property) being translatable into a
/// JsonApiDotNetCore/EF Core filter expression. Fine at the scale this app expects (ADR-0024's remarks).
/// </para>
/// </remarks>
public sealed class ApiDirectorClient(InternalApiClient apiClient)
{
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const string UsersPath = "/api/users";
    private const string RoleGrantsPath = "/api/roleGrants";
    private const string UsersResourceType = "users";
    private const string RoleGrantsResourceType = "roleGrants";

    /// <remarks>
    /// Matches <c>Events.ApiEventClient</c>'s own options - without <c>WhenWritingNull</c>, a create request
    /// serializes <see cref="RoleGrantResourceObject.Id"/> (always null on a POST) as an explicit
    /// <c>"id":null</c>, which Api's JsonApiDotNetCore pipeline rejects rather than treating as omitted.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <remarks>
    /// One page covers this app's expected scale (ADR-0024) - see this type's own remarks for why a true
    /// server-side page/filter isn't used instead.
    /// </remarks>
    private const int MaxFetch = 1000;

    /// <summary>Lists Users, joined with their Role/Grants, filtered and paginated client-side.</summary>
    /// <param name="pageNumber">The 1-based page to return.</param>
    /// <param name="pageSize">The number of rows per page.</param>
    /// <param name="search">A case-insensitive substring match against email or display name, or <see langword="null"/> for no filter.</param>
    /// <param name="state">Restricts to <see cref="UserState.Active"/>/<see cref="UserState.Invited"/> Users, or <see langword="null"/> for all.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP calls.</param>
    public async Task<(IReadOnlyList<UserRowDto> Users, int Total)> GetUsersAsync(
        int pageNumber, int pageSize, string? search, UserState? state, CancellationToken cancellationToken)
    {
        IReadOnlyList<UserRowDto> all = await GetAllUsersJoinedAsync(cancellationToken);

        IEnumerable<UserRowDto> filtered = all;
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(row =>
                row.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (row.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (state is not null)
        {
            bool wantActive = state == UserState.Active;
            filtered = filtered.Where(row => row.HasCredential == wantActive);
        }

        List<UserRowDto> materialized = [.. filtered];
        List<UserRowDto> page = [.. materialized.Skip((pageNumber - 1) * pageSize).Take(pageSize)];
        return (page, materialized.Count);
    }

    /// <summary>Reads a single User by id, joined with their Role/Grants.</summary>
    public async Task<UserRowDto?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, $"{UsersPath}/{userId}");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        UserDocument document = await ReadAsync<UserDocument>(response, cancellationToken);
        IReadOnlyList<RoleGrantAttributesDto> grants = await GetGrantsForUsersAsync([userId], cancellationToken);
        return ToRow(document.Data, grants);
    }

    /// <summary>Lists the Directors currently granted access to <paramref name="eventId"/> (EventEditor's Directors section).</summary>
    public async Task<IReadOnlyList<UserRowDto>> GetDirectorsForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        List<string> userIds = await GetDirectorUserIdsForEventAsync(eventId, cancellationToken);
        if (userIds.Count == 0)
        {
            return [];
        }

        IReadOnlyList<UserRowDto> users = await GetUsersByIdAsync(userIds, cancellationToken);
        return users;
    }

    /// <summary>
    /// Counts the Directors currently granted access to <paramref name="eventId"/> - the Dashboard's
    /// delete-confirmation dialog (P2-17, #112). One round trip, unlike <see cref="GetDirectorsForEventAsync"/>'s
    /// two - a count doesn't need the second fetch that resolves ids into Users.
    /// </summary>
    public async Task<int> GetDirectorCountForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        List<string> userIds = await GetDirectorUserIdsForEventAsync(eventId, cancellationToken);
        return userIds.Count;
    }

    private async Task<List<string>> GetDirectorUserIdsForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        string filter = $"and(equals(eventId,'{eventId}'),equals(roleId,'{RoleIds.Director}'))";
        RoleGrantCollectionDocument grantsDocument = await FetchAsync<RoleGrantCollectionDocument>(
            $"{RoleGrantsPath}?filter={Uri.EscapeDataString(filter)}&page[size]={MaxFetch}", cancellationToken);
        return [.. grantsDocument.Data
            .Select(resource => resource.Attributes!.UserId).OfType<string>().Distinct()];
    }

    /// <summary>Grants a User the unscoped, platform-wide Director Role (ADR-0035) - the act an Invite performs.</summary>
    public Task<GrantWriteOutcome> GrantDirectorRoleAsync(string userId, CancellationToken cancellationToken) =>
        CreateGrantAsync(userId, RoleIds.Director, eventId: null, cancellationToken);

    /// <summary>Grants a User who already holds the Director Role access to one more Event.</summary>
    public Task<GrantWriteOutcome> GrantEventAccessAsync(string userId, Guid eventId, CancellationToken cancellationToken) =>
        CreateGrantAsync(userId, RoleIds.Director, eventId, cancellationToken);

    private async Task<GrantWriteOutcome> CreateGrantAsync(
        string userId, int roleId, Guid? eventId, CancellationToken cancellationToken)
    {
        var body = new RoleGrantDocument
        {
            Data = new RoleGrantResourceObject
            {
                Type = RoleGrantsResourceType,
                Attributes = new RoleGrantAttributesDto { UserId = userId, RoleId = roleId, EventId = eventId }
            }
        };
        using var request = NewRequest(HttpMethod.Post, RoleGrantsPath, body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return GrantWriteOutcome.Forbidden;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return GrantWriteOutcome.AlreadyGranted;
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        return GrantWriteOutcome.Created;
    }

    private async Task<IReadOnlyList<UserRowDto>> GetAllUsersJoinedAsync(CancellationToken cancellationToken)
    {
        UserCollectionDocument usersDocument = await FetchAsync<UserCollectionDocument>(
            $"{UsersPath}?page[size]={MaxFetch}", cancellationToken);
        RoleGrantCollectionDocument grantsDocument = await FetchAsync<RoleGrantCollectionDocument>(
            $"{RoleGrantsPath}?page[size]={MaxFetch}", cancellationToken);

        return JoinUsersWithGrants(usersDocument, grantsDocument.Data.Select(resource => resource.Attributes!));
    }

    private async Task<IReadOnlyList<RoleGrantAttributesDto>> GetGrantsForUsersAsync(
        IReadOnlyList<string> userIds, CancellationToken cancellationToken)
    {
        string filter = BuildIdFilter("userId", userIds);
        RoleGrantCollectionDocument document = await FetchAsync<RoleGrantCollectionDocument>(
            $"{RoleGrantsPath}?filter={Uri.EscapeDataString(filter)}&page[size]={MaxFetch}", cancellationToken);
        return [.. document.Data.Select(resource => resource.Attributes!)];
    }

    private async Task<IReadOnlyList<UserRowDto>> GetUsersByIdAsync(
        IReadOnlyList<string> userIds, CancellationToken cancellationToken)
    {
        string filter = BuildIdFilter("id", userIds);
        UserCollectionDocument usersDocument = await FetchAsync<UserCollectionDocument>(
            $"{UsersPath}?filter={Uri.EscapeDataString(filter)}&page[size]={MaxFetch}", cancellationToken);

        IReadOnlyList<RoleGrantAttributesDto> grants = await GetGrantsForUsersAsync(userIds, cancellationToken);
        return JoinUsersWithGrants(usersDocument, grants);
    }

    /// <remarks>
    /// Shared by <see cref="GetGrantsForUsersAsync"/> (filters <c>roleGrants</c> on <c>userId</c>) and
    /// <see cref="GetUsersByIdAsync"/> (filters <c>users</c> on <c>id</c>) - JSON:API's <c>any()</c> operator
    /// only accepts 2+ values, so a single id still needs the plain <c>equals()</c> form.
    /// </remarks>
    private static string BuildIdFilter(string field, IReadOnlyList<string> ids) =>
        ids.Count == 1
            ? $"equals({field},'{ids[0]}')"
            : $"any({field},{string.Join(",", ids.Select(id => $"'{id}'"))})";

    /// <summary>The fetch-users, fetch-grants, join-by-user-id shape common to <see cref="GetAllUsersJoinedAsync"/> and <see cref="GetUsersByIdAsync"/>, differing only in how each already scoped its two fetches.</summary>
    private static IReadOnlyList<UserRowDto> JoinUsersWithGrants(
        UserCollectionDocument usersDocument, IEnumerable<RoleGrantAttributesDto> grants)
    {
        var grantsByUserId = grants.Where(g => g.UserId is not null).ToLookup(g => g.UserId!);
        return [.. usersDocument.Data.Select(user => ToRow(user, grantsByUserId[user.Id!]))];
    }

    private static UserRowDto ToRow(UserResourceObject user, IEnumerable<RoleGrantAttributesDto> grants)
    {
        List<RoleGrantAttributesDto> materialized = [.. grants];
        return new UserRowDto
        {
            Id = user.Id!,
            Email = user.Attributes!.Email!,
            DisplayName = user.Attributes.DisplayName,
            HasCredential = user.Attributes.HasCredential ?? false,
            IsAdmin = materialized.Any(g => g.RoleId == RoleIds.Admin),
            IsDirector = materialized.Any(g => g.RoleId == RoleIds.Director),
            EventGrantCount = materialized.Count(g => g.RoleId == RoleIds.Director && g.EventId is not null)
        };
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri, object? body = null)
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

    /// <remarks>Same discipline as <c>Events.ApiEventClient.SendAsync</c> - see its remarks.</remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not AuthorizationDataUnavailableException && !cancellationToken.IsCancellationRequested)
        {
            throw new DirectorDataUnavailableException("The Director store (Api) is unreachable.", ex);
        }
    }

    /// <summary>The GET-then-expect-200-then-deserialize shape shared by every read in this class other than <see cref="GetUserAsync"/>, which needs to inspect the status before deciding.</summary>
    private async Task<T> FetchAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, uri);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        EnsureExpectedStatus(response, HttpStatusCode.OK);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private static void EnsureExpectedStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw new DirectorDataUnavailableException(
                $"The Director store (Api) returned an unexpected {(int)response.StatusCode} response.",
                new HttpRequestException(response.ReasonPhrase));
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken))!;
}
