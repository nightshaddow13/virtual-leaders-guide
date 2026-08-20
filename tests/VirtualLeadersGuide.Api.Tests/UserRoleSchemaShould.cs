using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P2-3's (#12) acceptance criteria and ADR-0017's filtered-unique-index
/// requirement, exercised directly against the DbContext rather than through HTTP - see
/// <c>InternalAuthorizationEndpointsShould</c> for the endpoint-level equivalent. ADR-0014:
/// <c>EnsureCreatedAsync</c> builds this schema from the current EF model on SQLite, not by replaying the
/// real SQL Server migration, so this is also what proves the filtered indexes (SQL Server-flavored
/// <c>HasFilter</c> syntax) actually apply under SQLite too.
/// </remarks>
public class UserRoleSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task SeedAdminAndDirectorRoles_WhenTheSchemaIsCreated_ForEnsureCreated()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        List<Role> roles = await db.DomainRoles.AsNoTracking().ToListAsync();

        Assert.Contains(roles, r => r.Id == RoleIds.Admin && r.Name == RoleNames.Admin);
        Assert.Contains(roles, r => r.Id == RoleIds.Director && r.Name == RoleNames.Director);
    }

    [Fact]
    public async Task SucceedWithOneRowPerEvent_WhenAUserHoldsTheSameRoleOnMultipleEvents_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ApplicationUser user = await _factory.CreateUserAsync();
        Event eventA = await _factory.CreateEventAsync();
        Event eventB = await _factory.CreateEventAsync();

        db.DomainUserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Director, EventId = eventA.Id },
            new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Director, EventId = eventB.Id });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DomainUserRoles.CountAsync(g => g.UserId == user.Id));
    }

    [Fact]
    public async Task SucceedWithOneRowPerDirector_WhenAnEventHasMultipleDirectors_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ApplicationUser userA = await _factory.CreateUserAsync();
        ApplicationUser userB = await _factory.CreateUserAsync();
        Event @event = await _factory.CreateEventAsync();
        var eventId = @event.Id;

        db.DomainUserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), UserId = userA.Id, RoleId = RoleIds.Director, EventId = eventId },
            new UserRole { Id = Guid.NewGuid(), UserId = userB.Id, RoleId = RoleIds.Director, EventId = eventId });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DomainUserRoles.CountAsync(g => g.EventId == eventId));
    }

    [Fact]
    public async Task HaveNoEventId_WhenTheGrantIsPlatformWide_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ApplicationUser user = await _factory.CreateUserAsync();

        db.DomainUserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Admin });
        await db.SaveChangesAsync();

        UserRole grant = await db.DomainUserRoles.AsNoTracking().SingleAsync(g => g.UserId == user.Id);
        Assert.Null(grant.EventId);
    }

    [Fact]
    public async Task AllowBothGrants_WhenAUserHoldsAPlatformWideAndAnEventScopedGrantForTheSameRole_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ApplicationUser user = await _factory.CreateUserAsync();
        Event @event = await _factory.CreateEventAsync();

        db.DomainUserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Director, EventId = null },
            new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Director, EventId = @event.Id });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DomainUserRoles.CountAsync(g => g.UserId == user.Id));
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheSamePlatformWideGrantIsAddedTwice_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ApplicationUser user = await _factory.CreateUserAsync();

        db.DomainUserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Admin });
        await db.SaveChangesAsync();

        db.DomainUserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Admin });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheSameEventScopedGrantIsAddedTwice_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ApplicationUser user = await _factory.CreateUserAsync();
        Event @event = await _factory.CreateEventAsync();
        var eventId = @event.Id;

        db.DomainUserRoles.Add(
            new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Director, EventId = eventId });
        await db.SaveChangesAsync();

        db.DomainUserRoles.Add(
            new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Director, EventId = eventId });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CascadeDeleteGrants_WhenTheUnderlyingApplicationUserIsDeleted_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ApplicationUser user = await _factory.CreateUserAsync();
        db.DomainUserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = RoleIds.Admin });
        await db.SaveChangesAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        Assert.False(await db.DomainUserRoles.AnyAsync(g => g.UserId == user.Id));
    }

}
