using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

public class ApiUserStoreShould
{
    [Fact]
    public async Task ReturnTheMappedUser_WhenApiRespondsWithOk_ForFindByIdAsync()
    {
        var dto = NewDto();
        var store = new ApiUserStore(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, dto)));

        ApplicationUser? user = await store.FindByIdAsync(dto.Id, CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal(dto.Id, user!.Id);
        Assert.Equal(dto.Email, user.Email);
        Assert.Equal(dto.ConcurrencyStamp, user.ConcurrencyStamp);
    }

    [Fact]
    public async Task ReturnNull_WhenApiRespondsWithNotFound_ForFindByIdAsync()
    {
        var store = new ApiUserStore(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));

        ApplicationUser? user = await store.FindByIdAsync("missing", CancellationToken.None);

        Assert.Null(user);
    }

    [Fact]
    public async Task SucceedAndUpdateTheCallersConcurrencyStamp_WhenApiRespondsWithOk_ForUpdateAsync()
    {
        var user = new ApplicationUser { Id = "abc", ConcurrencyStamp = "old-stamp" };
        var returnedDto = NewDto(id: user.Id);
        returnedDto.ConcurrencyStamp = "new-stamp-from-api";
        var store = new ApiUserStore(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, returnedDto)));

        IdentityResult result = await store.UpdateAsync(user, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("new-stamp-from-api", user.ConcurrencyStamp);
    }

    [Fact]
    public async Task ReturnConcurrencyFailure_WhenApiRespondsWithConflict_ForUpdateAsync()
    {
        var user = new ApplicationUser { Id = "abc", ConcurrencyStamp = "stale-stamp" };
        var store = new ApiUserStore(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Conflict)));

        IdentityResult result = await store.UpdateAsync(user, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ConcurrencyFailure");
    }

    [Fact]
    public async Task ReturnDuplicateUserName_WhenApiRespondsWithConflict_ForCreateAsync()
    {
        var user = new ApplicationUser { Id = "abc", UserName = "taken@example.com", ConcurrencyStamp = "stamp" };
        var store = new ApiUserStore(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Conflict)));

        IdentityResult result = await store.CreateAsync(user, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "DuplicateUserName");
    }

    [Fact]
    public async Task ThrowIdentityStoreUnavailableException_WhenTheHttpCallFails_ForFindByIdAsync()
    {
        var store = new ApiUserStore(new StubHttpClientFactory(StubHttpMessageHandler.ThrowingOn(
            () => new HttpRequestException("simulated Api outage"))));

        await Assert.ThrowsAsync<IdentityStoreUnavailableException>(
            () => store.FindByIdAsync("abc", CancellationToken.None));
    }

    [Fact]
    public async Task ThrowIdentityStoreUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForFindByIdAsync()
    {
        var store = new ApiUserStore(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<IdentityStoreUnavailableException>(
            () => store.FindByIdAsync("abc", CancellationToken.None));
    }

    private static IdentityUserDto NewDto(string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString(),
        UserName = "person@example.com",
        NormalizedUserName = "PERSON@EXAMPLE.COM",
        Email = "person@example.com",
        NormalizedEmail = "PERSON@EXAMPLE.COM",
        EmailConfirmed = true,
        PasswordHash = "hash",
        SecurityStamp = "stamp",
        ConcurrencyStamp = "concurrency-stamp",
        LockoutEnabled = true
    };
}
