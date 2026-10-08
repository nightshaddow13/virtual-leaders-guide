using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P5-11's (#96) <c>Sections</c> schema: both Create factories' parent shape,
/// the Name check constraint, <c>CK_Sections_ExactlyOneParent</c> (ADR-0046's P5-11 amendment), and the
/// <c>Sections</c>→<c>Tabs</c>/<c>SubTabs</c> Restrict - exercised directly against the DbContext.
/// </remarks>
public class SectionSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task RoundTripParentTabId_WhenASectionIsCreatedUnderATab_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);

        Section section = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id, "Waterfront");

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Section reloaded = await db.Sections.AsNoTracking().SingleAsync(s => s.Id == section.Id);
        Assert.Equal(tab.Id, reloaded.ParentTabId);
        Assert.Null(reloaded.ParentSubTabId);
        Assert.Equal("Waterfront", reloaded.Name);
    }

    [Fact]
    public async Task RoundTripParentSubTabId_WhenASectionIsCreatedUnderASubTab_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id);

        Section section = await _factory.CreateSectionUnderSubTabAsync(@event.Id, subTab.Id, "Kayaking");

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Section reloaded = await db.Sections.AsNoTracking().SingleAsync(s => s.Id == section.Id);
        Assert.Null(reloaded.ParentTabId);
        Assert.Equal(subTab.Id, reloaded.ParentSubTabId);
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenSectionNameIsAllWhitespace_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Sections.Add(Section.CreateUnderTab(@event.Id, tab.Id, "   ", 0));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenNeitherParentIsSet_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Sections.Add(new Section { Id = Guid.NewGuid(), EventId = @event.Id, Name = "Waterfront", SortOrder = 0 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenBothParentsAreSet_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Sections.Add(new Section
        {
            Id = Guid.NewGuid(), EventId = @event.Id, ParentTabId = tab.Id, ParentSubTabId = subTab.Id, Name = "Waterfront", SortOrder = 0
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheReferencedParentTabIsDeletedWhileASectionStillReferencesIt_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Tabs.Remove(await db.Tabs.SingleAsync(t => t.Id == tab.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
