using System.Net;
using System.Net.Http.Json;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

// Every case here drives _client, created via CreateAuthenticatedClient() (X-Internal-Key only, no bearer
// token) - deliberately, since /internal/authorization/* stays off the RequireInternalUser policy (P2-5,
// #14): it's the endpoint that produces a JWT's claims in the first place, so requiring one would be
// circular. That every case below still succeeds pins the amendment in
// docs/adr/0015-internal-key-validated-via-authentication-handler.md.
public class InternalAuthorizationEndpointsShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateAuthenticatedClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task SucceedWithMatchingData_WhenGrantingAndListingAcrossPlatformWideAndEventScopedGrants_ForFullLifecycle()
    {
        string userId = await CreateUserAsync();
        // P2-6, #15: UserRoles.EventId is now a real FK - a request naming an EventId with no matching Events
        // row would fail (see EventSchemaShould's cascade-delete coverage for the FK itself), so this test's
        // fixture needs real Events, not just any Guid.
        Guid eventAId = (await _factory.CreateEventAsync()).Id;
        Guid eventBId = (await _factory.CreateEventAsync()).Id;

        RoleGrantDto adminGrant = await CreateGrantAsync(userId, RoleIds.Admin, eventId: null);
        Assert.Equal(RoleNames.Admin, adminGrant.RoleName);
        Assert.Null(adminGrant.EventId);

        RoleGrantDto directorGrantA = await CreateGrantAsync(userId, RoleIds.Director, eventAId);
        RoleGrantDto directorGrantB = await CreateGrantAsync(userId, RoleIds.Director, eventBId);
        Assert.Equal(eventAId, directorGrantA.EventId);
        Assert.Equal(eventBId, directorGrantB.EventId);

        HttpResponseMessage listResponse = await _client.GetAsync(InternalAuthorizationRoutes.ForUserGrants(userId));
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        List<RoleGrantDto>? grants = await listResponse.Content.ReadFromJsonAsync<List<RoleGrantDto>>();
        Assert.Equal(3, grants!.Count);

        // Duplicate platform-wide grant - the filtered unique index rejects it (ADR-0017).
        HttpResponseMessage duplicateAdminResponse = await _client.PostAsJsonAsync(
            InternalAuthorizationRoutes.ForUserGrants(userId),
            new CreateRoleGrantRequest { RoleId = RoleIds.Admin });
        Assert.Equal(HttpStatusCode.Conflict, duplicateAdminResponse.StatusCode);

        // Duplicate Event-scoped grant, same Event - the other filtered unique index rejects it.
        HttpResponseMessage duplicateDirectorResponse = await _client.PostAsJsonAsync(
            InternalAuthorizationRoutes.ForUserGrants(userId),
            new CreateRoleGrantRequest { RoleId = RoleIds.Director, EventId = eventAId });
        Assert.Equal(HttpStatusCode.Conflict, duplicateDirectorResponse.StatusCode);

        HttpResponseMessage deleteResponse = await _client.DeleteAsync(
            InternalAuthorizationRoutes.ForUserGrantById(userId, directorGrantA.Id));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage listAfterDeleteResponse = await _client.GetAsync(
            InternalAuthorizationRoutes.ForUserGrants(userId));
        List<RoleGrantDto>? grantsAfterDelete = await listAfterDeleteResponse.Content
            .ReadFromJsonAsync<List<RoleGrantDto>>();
        Assert.Equal(2, grantsAfterDelete!.Count);
    }

    [Fact]
    public async Task ReturnNotFound_WhenNoUserMatchesTheGivenId_ForGetGrants()
    {
        HttpResponseMessage response = await _client.GetAsync(
            InternalAuthorizationRoutes.ForUserGrants(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnNotFound_WhenNoUserMatchesTheGivenId_ForCreateGrant()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            InternalAuthorizationRoutes.ForUserGrants(Guid.NewGuid().ToString()),
            new CreateRoleGrantRequest { RoleId = RoleIds.Admin });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnNotFound_WhenNoRoleMatchesTheGivenRoleId_ForCreateGrant()
    {
        string userId = await CreateUserAsync();

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            InternalAuthorizationRoutes.ForUserGrants(userId),
            new CreateRoleGrantRequest { RoleId = -1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnNotFound_WhenNoGrantMatchesTheGivenId_ForDeleteGrant()
    {
        string userId = await CreateUserAsync();

        HttpResponseMessage response = await _client.DeleteAsync(
            InternalAuthorizationRoutes.ForUserGrantById(userId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnUnauthorized_WhenXInternalKeyHeaderIsMissing_ForAnyAuthorizationEndpoint()
    {
        using HttpClient unauthenticatedClient = _factory.CreateClient();

        HttpResponseMessage response = await unauthenticatedClient.GetAsync(
            InternalAuthorizationRoutes.ForUserGrants(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> CreateUserAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var dto = new IdentityUserDto
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            PasswordHash = "initial-hash",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync(InternalIdentityRoutes.ForUsers(), dto);
        response.EnsureSuccessStatusCode();
        return dto.Id;
    }

    private async Task<RoleGrantDto> CreateGrantAsync(string userId, int roleId, Guid? eventId)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            InternalAuthorizationRoutes.ForUserGrants(userId),
            new CreateRoleGrantRequest { RoleId = roleId, EventId = eventId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoleGrantDto>())!;
    }
}
