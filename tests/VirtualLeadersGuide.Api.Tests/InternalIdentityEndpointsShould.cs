using System.Net;
using System.Net.Http.Json;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

public class InternalIdentityEndpointsShould : IAsyncLifetime
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
    public async Task SucceedWithMatchingData_WhenPerformingFullCrudLifecycle_ForInternalIdentityUsersEndpoint()
    {
        IdentityUserDto created = NewUserDto();

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(InternalIdentityRoutes.ForUsers(), created);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        HttpResponseMessage getByIdResponse = await _client.GetAsync(InternalIdentityRoutes.ForUserById(created.Id));
        Assert.Equal(HttpStatusCode.OK, getByIdResponse.StatusCode);
        IdentityUserDto? fetched = await getByIdResponse.Content.ReadFromJsonAsync<IdentityUserDto>();
        Assert.Equal(created.Email, fetched?.Email);

        HttpResponseMessage getByNameResponse = await _client.GetAsync(
            InternalIdentityRoutes.ForUserByNormalizedUserName(created.NormalizedUserName!));
        Assert.Equal(HttpStatusCode.OK, getByNameResponse.StatusCode);

        HttpResponseMessage getByEmailResponse = await _client.GetAsync(
            InternalIdentityRoutes.ForUserByNormalizedEmail(created.NormalizedEmail!));
        Assert.Equal(HttpStatusCode.OK, getByEmailResponse.StatusCode);

        var updated = new IdentityUserDto
        {
            Id = created.Id,
            UserName = created.UserName,
            NormalizedUserName = created.NormalizedUserName,
            Email = created.Email,
            NormalizedEmail = created.NormalizedEmail,
            EmailConfirmed = true,
            PasswordHash = "updated-hash",
            SecurityStamp = created.SecurityStamp,
            ConcurrencyStamp = fetched!.ConcurrencyStamp,
            PhoneNumberConfirmed = false,
            LockoutEnabled = true,
            AccessFailedCount = 0
        };

        HttpResponseMessage updateResponse = await _client.PutAsJsonAsync(
            InternalIdentityRoutes.ForUserById(created.Id), updated);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        IdentityUserDto? afterUpdate = await updateResponse.Content.ReadFromJsonAsync<IdentityUserDto>();
        Assert.True(afterUpdate!.EmailConfirmed);
        Assert.NotEqual(fetched.ConcurrencyStamp, afterUpdate.ConcurrencyStamp);

        HttpResponseMessage deleteResponse = await _client.DeleteAsync(InternalIdentityRoutes.ForUserById(created.Id));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage verifyDeletedResponse = await _client.GetAsync(InternalIdentityRoutes.ForUserById(created.Id));
        Assert.Equal(HttpStatusCode.NotFound, verifyDeletedResponse.StatusCode);
    }

    [Fact]
    public async Task ReturnNotFound_WhenNoUserMatchesTheGivenId_ForGetById()
    {
        HttpResponseMessage response = await _client.GetAsync(InternalIdentityRoutes.ForUserById(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnConflict_WhenTheConcurrencyStampIsStale_ForUpdate()
    {
        IdentityUserDto created = NewUserDto();
        await _client.PostAsJsonAsync(InternalIdentityRoutes.ForUsers(), created);

        // created.ConcurrencyStamp is the value from before insert - still fine for a first update, but
        // reused here unchanged for a *second* one below to simulate a caller acting on a stale read.
        HttpResponseMessage firstUpdateResponse = await _client.PutAsJsonAsync(
            InternalIdentityRoutes.ForUserById(created.Id), CopyWith(created, emailConfirmed: true));
        Assert.Equal(HttpStatusCode.OK, firstUpdateResponse.StatusCode);

        HttpResponseMessage response = await _client.PutAsJsonAsync(
            InternalIdentityRoutes.ForUserById(created.Id), CopyWith(created, phoneNumberConfirmed: true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReturnConflict_WhenTheNormalizedUserNameAlreadyExists_ForCreate()
    {
        IdentityUserDto first = NewUserDto();
        await _client.PostAsJsonAsync(InternalIdentityRoutes.ForUsers(), first);

        IdentityUserDto duplicate = NewUserDto(email: first.Email!);

        HttpResponseMessage response = await _client.PostAsJsonAsync(InternalIdentityRoutes.ForUsers(), duplicate);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReturnUnauthorized_WhenXInternalKeyHeaderIsMissing_ForAnyIdentityEndpoint()
    {
        using HttpClient unauthenticatedClient = _factory.CreateClient();

        HttpResponseMessage response = await unauthenticatedClient.GetAsync(
            InternalIdentityRoutes.ForUserById(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static IdentityUserDto CopyWith(
        IdentityUserDto source, bool? emailConfirmed = null, bool? phoneNumberConfirmed = null) => new()
    {
        Id = source.Id,
        UserName = source.UserName,
        NormalizedUserName = source.NormalizedUserName,
        Email = source.Email,
        NormalizedEmail = source.NormalizedEmail,
        EmailConfirmed = emailConfirmed ?? source.EmailConfirmed,
        PasswordHash = source.PasswordHash,
        SecurityStamp = source.SecurityStamp,
        ConcurrencyStamp = source.ConcurrencyStamp,
        PhoneNumber = source.PhoneNumber,
        PhoneNumberConfirmed = phoneNumberConfirmed ?? source.PhoneNumberConfirmed,
        TwoFactorEnabled = source.TwoFactorEnabled,
        LockoutEnd = source.LockoutEnd,
        LockoutEnabled = source.LockoutEnabled,
        AccessFailedCount = source.AccessFailedCount
    };

    private static IdentityUserDto NewUserDto(string? email = null)
    {
        email ??= $"{Guid.NewGuid()}@example.com";
        return new IdentityUserDto
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
    }
}
