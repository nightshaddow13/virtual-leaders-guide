using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Exercises <see cref="AdminAllowlistSynchronizer.SyncAsync"/>'s promote/demote decision (ADR-0008)
/// against <see cref="FakeAuthorizationApiHandler"/>, which actually tracks grant state across
/// GET/POST/DELETE calls - unlike <see cref="StubHttpMessageHandler"/>'s one-canned-response-per-test
/// model, which can't express "read current grants, then write a change".
/// </remarks>
public class AdminAllowlistSynchronizerShould
{
    private const string UserId = "user-1";
    private const string Email = "admin@example.com";

    [Fact]
    public async Task CreateThePlatformWideAdminGrant_WhenTheEmailIsAllowlistedAndNotYetAdmin_ForSyncAsync()
    {
        var handler = new FakeAuthorizationApiHandler();
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: Email);

        IReadOnlyList<RoleGrantDto>? grants = await synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None);

        RoleGrantDto grant = Assert.Single(grants!);
        Assert.Equal(RoleIds.Admin, grant.RoleId);
        Assert.Null(grant.EventId);
    }

    [Fact]
    public async Task LeaveGrantsUnchanged_WhenTheEmailIsAllowlistedAndAlreadyAdmin_ForSyncAsync()
    {
        var handler = new FakeAuthorizationApiHandler();
        RoleGrantDto existing = handler.SeedAdminGrant(UserId);
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: Email);

        IReadOnlyList<RoleGrantDto>? grants = await synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None);

        RoleGrantDto grant = Assert.Single(grants!);
        Assert.Equal(existing.Id, grant.Id);
    }

    [Fact]
    public async Task DeleteThePlatformWideAdminGrant_WhenTheEmailIsNoLongerAllowlisted_ForSyncAsync()
    {
        var handler = new FakeAuthorizationApiHandler();
        handler.SeedAdminGrant(UserId);
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: "someone-else@example.com");

        IReadOnlyList<RoleGrantDto>? grants = await synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None);

        Assert.Empty(grants!);
    }

    [Fact]
    public async Task DemoteTheLastAdmin_WhenTheAllowlistIsEmpty_ForSyncAsync()
    {
        var handler = new FakeAuthorizationApiHandler();
        handler.SeedAdminGrant(UserId);
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: string.Empty);

        IReadOnlyList<RoleGrantDto>? grants = await synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None);

        Assert.Empty(grants!);
    }

    [Fact]
    public async Task LeaveEventScopedGrantsIntact_WhenDemotingAnAdmin_ForSyncAsync()
    {
        var handler = new FakeAuthorizationApiHandler();
        handler.SeedAdminGrant(UserId);
        RoleGrantDto directorGrant = handler.SeedDirectorGrant(UserId, Guid.NewGuid());
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: string.Empty);

        IReadOnlyList<RoleGrantDto>? grants = await synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None);

        RoleGrantDto remaining = Assert.Single(grants!);
        Assert.Equal(directorGrant.Id, remaining.Id);
    }

    [Fact]
    public async Task MatchTheEmail_WhenTheAllowlistCasingAndWhitespaceDiffer_ForSyncAsync()
    {
        var handler = new FakeAuthorizationApiHandler();
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: "  ADMIN@EXAMPLE.COM  ");

        IReadOnlyList<RoleGrantDto>? grants = await synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None);

        Assert.Single(grants!);
    }

    [Fact]
    public async Task IgnoreBlankEntries_WhenTheAllowlistHasTrailingSeparators_ForSyncAsync()
    {
        var handler = new FakeAuthorizationApiHandler();
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: $"{Email};;,");

        IReadOnlyList<RoleGrantDto>? grants = await synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None);

        Assert.Single(grants!);
    }

    [Fact]
    public async Task ReturnNullWithoutWriting_WhenTheUserRowNoLongerExistsOnApi_ForSyncAsync()
    {
        FakeAuthorizationApiHandler handler = FakeAuthorizationApiHandler.WithUserNotFound();
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: Email);

        IReadOnlyList<RoleGrantDto>? grants = await synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None);

        Assert.Null(grants);
    }

    [Fact]
    public async Task Throw_WhenTheAuthorizationStoreIsUnreachable_ForSyncAsync()
    {
        StubHttpMessageHandler handler =
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("network down"));
        AdminAllowlistSynchronizer synchronizer = CreateSynchronizer(handler, allowlist: Email);

        await Assert.ThrowsAsync<AuthorizationDataUnavailableException>(
            () => synchronizer.SyncAsync(CreateUser(Email), CancellationToken.None));
    }

    private static ApplicationUser CreateUser(string email)
    {
        var normalizer = new UpperInvariantLookupNormalizer();
        return new ApplicationUser
        {
            Id = UserId,
            UserName = email,
            Email = email,
            NormalizedEmail = normalizer.NormalizeEmail(email)
        };
    }

    private static AdminAllowlistSynchronizer CreateSynchronizer(HttpMessageHandler handler, string allowlist) =>
        new(
            new ApiRoleGrantClient(new StubHttpClientFactory(handler)),
            Options.Create(new AdminAllowlistOptions { Emails = allowlist }),
            new UpperInvariantLookupNormalizer(),
            NullLogger<AdminAllowlistSynchronizer>.Instance);
}
