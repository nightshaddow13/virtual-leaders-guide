using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P5-11's (#96) <c>ActivityPlacements</c> schema: the six filtered unique
/// indexes backstopping ADR-0046's path-uniqueness rule, <c>CK_ActivityPlacements_SubSectionRequiresSection</c>,
/// the <c>ActivityPlacements</c>→<c>Activities</c> cascade (and that <see cref="ActivityPlacement.EventId"/>
/// deliberately carries no foreign key of its own - ADR-0072's remarks on
/// <see cref="VirtualLeadersGuideDbContext.ConfigureActivityPlacements"/> explain why a second cascade path
/// would be rejected by SQL Server), and the Restrict behavior on every Tier FK - exercised directly
/// against the DbContext.
/// </remarks>
public class ActivityPlacementSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task RoundTripFullTierPath_WhenAPlacementIsSaved_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id);
        Section section = await _factory.CreateSectionUnderSubTabAsync(@event.Id, subTab.Id);
        SubSection subSection = await _factory.CreateSubSectionAsync(@event.Id, section.Id);

        ActivityPlacement placement = await _factory.CreateActivityPlacementAsync(
            @event.Id, activity.Id, tab.Id, subTab.Id, section.Id, subSection.Id, sortOrder: 3);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        ActivityPlacement reloaded = await db.ActivityPlacements.AsNoTracking().SingleAsync(p => p.Id == placement.Id);
        Assert.Equal(@event.Id, reloaded.EventId);
        Assert.Equal(activity.Id, reloaded.ActivityId);
        Assert.Equal(tab.Id, reloaded.TabId);
        Assert.Equal(subTab.Id, reloaded.SubTabId);
        Assert.Equal(section.Id, reloaded.SectionId);
        Assert.Equal(subSection.Id, reloaded.SubSectionId);
        Assert.Equal(3, reloaded.SortOrder);
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenSubSectionIsSetWithNoSection_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        Section section = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id);
        SubSection subSection = await _factory.CreateSubSectionAsync(@event.Id, section.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.ActivityPlacements.Add(new ActivityPlacement
        {
            Id = Guid.NewGuid(), EventId = @event.Id, ActivityId = activity.Id, TabId = tab.Id, SubSectionId = subSection.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheReferencedTabIsDeletedWhileAPlacementStillReferencesIt_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Tabs.Remove(await db.Tabs.SingleAsync(t => t.Id == tab.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <remarks>P5-9's (#95) eventual Activity delete takes every one of its Placements with it - the single cascade path <see cref="VirtualLeadersGuideDbContext.ConfigureActivityPlacements"/>'s remarks describe.</remarks>
    [Fact]
    public async Task DeleteThePlacement_WhenItsActivityIsDeleted_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        ActivityPlacement placement = await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Activities.Remove(await db.Activities.SingleAsync(a => a.Id == activity.Id));
        await db.SaveChangesAsync();

        Assert.False(await db.ActivityPlacements.AsNoTracking().AnyAsync(p => p.Id == placement.Id));
    }

    /// <remarks>
    /// Deleting the Event, which cascades to the Activity (<c>ConfigureActivities</c>), which cascades to
    /// the Placement above - proves <see cref="ActivityPlacement.EventId"/> carrying no FK of its own
    /// doesn't orphan a Placement when the whole Event goes away; the Activity path still reaches it.
    /// </remarks>
    [Fact]
    public async Task DeleteThePlacement_WhenItsEventIsDeleted_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        ActivityPlacement placement = await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Events.Remove(await db.Events.SingleAsync(e => e.Id == @event.Id));
        await db.SaveChangesAsync();

        Assert.False(await db.ActivityPlacements.AsNoTracking().AnyAsync(p => p.Id == placement.Id));
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheSameActivityIsPlacedTwiceOnTheBareTab_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.ActivityPlacements.Add(new ActivityPlacement { Id = Guid.NewGuid(), EventId = @event.Id, ActivityId = activity.Id, TabId = tab.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheSameActivityIsPlacedTwiceAtTheIdenticalFourLevelPath_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id);
        Section section = await _factory.CreateSectionUnderSubTabAsync(@event.Id, subTab.Id);
        SubSection subSection = await _factory.CreateSubSectionAsync(@event.Id, section.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id, subTab.Id, section.Id, subSection.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.ActivityPlacements.Add(new ActivityPlacement
        {
            Id = Guid.NewGuid(), EventId = @event.Id, ActivityId = activity.Id,
            TabId = tab.Id, SubTabId = subTab.Id, SectionId = section.Id, SubSectionId = subSection.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <remarks>Uniqueness is scoped per-Activity (ADR-0046) - two different Activities sharing the exact same Tier bucket is the normal, expected case (a Tab lists every Activity placed under it), not a conflict.</remarks>
    [Fact]
    public async Task AllowTwoDifferentActivities_WhenBothArePlacedOnTheSameBareTab_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity first = await _factory.CreateActivityAsync(@event.Id);
        Activity second = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, first.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.ActivityPlacements.Add(new ActivityPlacement { Id = Guid.NewGuid(), EventId = @event.Id, ActivityId = second.Id, TabId = tab.Id });

        await db.SaveChangesAsync();
        Assert.Equal(2, await db.ActivityPlacements.AsNoTracking().CountAsync(p => p.TabId == tab.Id));
    }

    /// <remarks>
    /// Exercises a different one of the six filtered unique indexes than the bare-Tab test above - a
    /// bare-Tab Placement and a Tab+Sub Tab Placement for the same Activity under the same Tab must not
    /// collide with each other (different <c>SubTabId</c> nullability shape, different index).
    /// </remarks>
    [Fact]
    public async Task AllowTheSameActivity_WhenPlacedOnTheBareTabAndOnASubTabOfThatSameTab_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Activity activity = await _factory.CreateActivityAsync(@event.Id);
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id);
        await _factory.CreateActivityPlacementAsync(@event.Id, activity.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.ActivityPlacements.Add(new ActivityPlacement
        {
            Id = Guid.NewGuid(), EventId = @event.Id, ActivityId = activity.Id, TabId = tab.Id, SubTabId = subTab.Id
        });

        await db.SaveChangesAsync();
        Assert.Equal(2, await db.ActivityPlacements.AsNoTracking().CountAsync(p => p.ActivityId == activity.Id));
    }
}
