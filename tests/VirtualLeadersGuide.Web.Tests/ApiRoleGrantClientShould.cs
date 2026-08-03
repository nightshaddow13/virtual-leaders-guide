using System.Net;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;

namespace VirtualLeadersGuide.Web.Tests;

public class ApiRoleGrantClientShould
{
    [Fact]
    public async Task ReturnTheMappedGrants_WhenApiRespondsWithOk_ForGetGrantsAsync()
    {
        var grants = new List<RoleGrantDto>
        {
            new() { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin, EventId = null }
        };
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, grants)));

        IReadOnlyList<RoleGrantDto>? result = await client.GetGrantsAsync("user-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(RoleNames.Admin, result[0].RoleName);
    }

    [Fact]
    public async Task ReturnNull_WhenApiRespondsWithNotFound_ForGetGrantsAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));

        IReadOnlyList<RoleGrantDto>? result = await client.GetGrantsAsync("missing", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ThrowAuthorizationDataUnavailableException_WhenTheHttpCallFails_ForGetGrantsAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(StubHttpMessageHandler.ThrowingOn(
            () => new HttpRequestException("simulated Api outage"))));

        await Assert.ThrowsAsync<AuthorizationDataUnavailableException>(
            () => client.GetGrantsAsync("user-1", CancellationToken.None));
    }

    [Fact]
    public async Task ThrowAuthorizationDataUnavailableException_WhenApiRespondsWithAnUnexpectedStatus_ForGetGrantsAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<AuthorizationDataUnavailableException>(
            () => client.GetGrantsAsync("user-1", CancellationToken.None));
    }

    [Fact]
    public async Task ReturnCreatedWithTheGrant_WhenApiRespondsWithCreated_ForCreateGrantAsync()
    {
        var dto = new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = Guid.NewGuid() };
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.Created, dto)));

        (GrantCreationOutcome outcome, RoleGrantDto? grant) =
            await client.CreateGrantAsync("user-1", RoleIds.Director, dto.EventId, CancellationToken.None);

        Assert.Equal(GrantCreationOutcome.Created, outcome);
        Assert.Equal(dto.Id, grant?.Id);
    }

    [Fact]
    public async Task ReturnAlreadyGranted_WhenApiRespondsWithConflict_ForCreateGrantAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Conflict)));

        (GrantCreationOutcome outcome, RoleGrantDto? grant) =
            await client.CreateGrantAsync("user-1", RoleIds.Admin, null, CancellationToken.None);

        Assert.Equal(GrantCreationOutcome.AlreadyGranted, outcome);
        Assert.Null(grant);
    }

    [Fact]
    public async Task ReturnUserOrRoleNotFound_WhenApiRespondsWithNotFound_ForCreateGrantAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));

        (GrantCreationOutcome outcome, RoleGrantDto? grant) =
            await client.CreateGrantAsync("missing", RoleIds.Admin, null, CancellationToken.None);

        Assert.Equal(GrantCreationOutcome.UserOrRoleNotFound, outcome);
        Assert.Null(grant);
    }

    [Fact]
    public async Task ReturnTrue_WhenApiRespondsWithNoContent_ForDeleteGrantAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NoContent)));

        bool result = await client.DeleteGrantAsync("user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ReturnFalse_WhenApiRespondsWithNotFound_ForDeleteGrantAsync()
    {
        var client = new ApiRoleGrantClient(new StubHttpClientFactory(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));

        bool result = await client.DeleteGrantAsync("user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }
}
