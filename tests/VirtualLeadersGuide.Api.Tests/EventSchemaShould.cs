using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P2-6's (#15) and P2-20's (#115) acceptance criteria - Slug uniqueness (Name
/// carries none, ADR-0053), the Name/Slug/Status CHECK constraints, the <c>UserRoles.EventId</c> FK, and
/// <see cref="Event.Status"/>'s default - exercised directly against the DbContext rather than through HTTP,
/// same pattern as <c>UserRoleSchemaShould</c>. ADR-0014: <c>EnsureCreatedAsync</c> builds this schema from
/// the current EF model on SQLite, not by replaying the real SQL Server migration, so this is also what
/// proves the SQL Server-flavored CHECK constraints (LIKE-based; see <see cref="VirtualLeadersGuideDbContext"/>)
/// parse under SQLite too. See <c>EventPasscodeShould</c> for <see cref="Event.Passcode"/>'s
/// encryption-at-rest coverage specifically.
/// </remarks>
public class EventSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task SaveSuccessfully_WhenNameAndSlugAreDistinct_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        db.Events.AddRange(
            Event.Create("Fall Retreat", "fall-retreat"), Event.Create("Spring Retreat", "spring-retreat"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Events.CountAsync());
    }

    /// <remarks>
    /// <see cref="Event.Name"/> carries no unique index at all (ADR-0053) - uniqueness among non-terminal
    /// Events is an application-level rule in <c>EventResourceDefinition.CheckForConflictsAsync</c>, exercised
    /// at the HTTP level by <see cref="EventsResourceShould"/>, not here. This is the DB-level proof that the
    /// index really is gone, not merely unused - a regression here would mean a future migration accidentally
    /// reintroduced it.
    /// </remarks>
    [Fact]
    public async Task SaveSuccessfully_WhenTwoEventsShareAName_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        db.Events.Add(Event.Create("Fall Retreat", "fall-retreat"));
        await db.SaveChangesAsync();

        db.Events.Add(Event.Create("Fall Retreat", "fall-retreat-2"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Events.CountAsync());
    }

    [Fact]
    public async Task DefaultToDraft_WhenAnEventIsCreatedWithoutAnExplicitStatus_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var @event = Event.Create("Fall Retreat", "fall-retreat");

        db.Events.Add(@event);
        await db.SaveChangesAsync();

        Assert.Equal(EventStatus.Draft, (await db.Events.FindAsync(@event.Id))!.Status);
    }

    /// <remarks>
    /// The database-level backstop for "<see cref="EventStatus.Past"/> is never stored" (ADR-0053) - since
    /// nothing in this app's own write path ever assigns <see cref="EventStatus.Past"/> to
    /// <see cref="Event.Status"/>, this constraint can only ever be exercised by writing it directly, as this
    /// test does. Also doubles as the proof <c>CK_Events_Status_Allowed</c>'s SQL parses under SQLite, not
    /// just SQL Server (ADR-0014) - see this class's own remarks.
    /// </remarks>
    [Fact]
    public async Task ThrowDbUpdateException_WhenStatusIsDirectlySetToPast_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var @event = Event.Create("Fall Retreat", "fall-retreat");
        @event.Status = EventStatus.Past;

        db.Events.Add(@event);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTwoEventsShareASlug_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        db.Events.Add(Event.Create("Fall Retreat", "retreat"));
        await db.SaveChangesAsync();

        db.Events.Add(Event.Create("Spring Retreat", "retreat"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenNameIsAllWhitespace_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        db.Events.Add(Event.Create("   ", "blank-name"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("double--hyphen")]
    [InlineData("has space")]
    [InlineData("has_underscore")]
    public async Task ThrowDbUpdateException_WhenSlugViolatesTheFormatConstraint_ForSaveChanges(string invalidSlug)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        db.Events.Add(Event.Create("Some Event", invalidSlug));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveSuccessfully_WhenEndIsAfterStart_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var @event = Event.Create("Fall Retreat", "fall-retreat");
        @event.StartsAt = DateTimeOffset.UtcNow;
        @event.EndsAt = @event.StartsAt.Value.AddDays(1);

        db.Events.Add(@event);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Events.CountAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenEndPrecedesStart_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var @event = Event.Create("Fall Retreat", "fall-retreat");
        @event.StartsAt = DateTimeOffset.UtcNow;
        @event.EndsAt = @event.StartsAt.Value.AddDays(-1);

        db.Events.Add(@event);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenEndEqualsStart_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var @event = Event.Create("Fall Retreat", "fall-retreat");
        @event.StartsAt = DateTimeOffset.UtcNow;
        @event.EndsAt = @event.StartsAt;

        db.Events.Add(@event);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenEndIsSetWithNoStart_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var @event = Event.Create("Fall Retreat", "fall-retreat");
        @event.EndsAt = DateTimeOffset.UtcNow;

        db.Events.Add(@event);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CascadeDeleteEventScopedGrants_WhenTheEventIsDeleted_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var @event = Event.Create("Fall Retreat", "fall-retreat");
        db.Events.Add(@event);
        ApplicationUser director = await AddUserAsync(db);
        db.DomainUserRoles.Add(
            new UserRole { Id = Guid.NewGuid(), UserId = director.Id, RoleId = RoleIds.Director, EventId = @event.Id });
        await db.SaveChangesAsync();

        db.Events.Remove(@event);
        await db.SaveChangesAsync();

        Assert.False(await db.DomainUserRoles.AnyAsync(g => g.EventId == @event.Id));
    }

    [Fact]
    public async Task LeavePlatformWideGrantsUntouched_WhenAnEventIsDeleted_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var @event = Event.Create("Fall Retreat", "fall-retreat");
        db.Events.Add(@event);
        ApplicationUser admin = await AddUserAsync(db);
        db.DomainUserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = admin.Id, RoleId = RoleIds.Admin });
        await db.SaveChangesAsync();

        db.Events.Remove(@event);
        await db.SaveChangesAsync();

        Assert.True(await db.DomainUserRoles.AnyAsync(g => g.UserId == admin.Id && g.EventId == null));
    }

    private static async Task<ApplicationUser> AddUserAsync(VirtualLeadersGuideDbContext db)
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
