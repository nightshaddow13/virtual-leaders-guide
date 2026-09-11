using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P5-15's (#20) acceptance criteria - the seeded <c>PageTypes</c> row, the
/// <c>Pages</c>→<c>InfoPages</c> Table-Per-Type split, <c>Page.PageTypeId</c> being readable off the base
/// table without joining a subtype table, and the <c>Pages</c>→<c>Events</c>/<c>Pages</c>→<c>PageTypes</c>
/// foreign keys - exercised directly against the DbContext rather than through HTTP, same pattern as
/// <c>EventSchemaShould</c>/<c>UserRoleSchemaShould</c>. ADR-0014: <c>EnsureCreatedAsync</c> builds this
/// schema from the current EF model on SQLite, not by replaying the real SQL Server migration, so this is
/// also what proves <c>CK_Pages_Title_NotEmpty</c> parses under SQLite too. See ADR-0055 for why TPT and why
/// <see cref="Page.PageTypeId"/> exists at all.
/// </remarks>
/// <remarks>
/// The referenced-<c>PageType</c>-deletion test seeds its InfoPage in a separate, disposed scope before
/// deleting in a fresh one - deleting a tracked <see cref="PageType"/> whose dependent <see cref="Page"/> is
/// already loaded into the same change tracker throws <see cref="InvalidOperationException"/> from EF's own
/// client-side fixup before <c>SaveChangesAsync</c> ever reaches the database; a fresh scope forces the
/// violation to surface as the intended <see cref="DbUpdateException"/> from the FK constraint instead.
/// </remarks>
public class PageSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task SeedInfoPageType_WhenTheSchemaIsCreated_ForEnsureCreated()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        List<PageType> pageTypes = await db.PageTypes.AsNoTracking().ToListAsync();

        Assert.Contains(pageTypes, pt => pt.Id == PageTypeIds.InfoPage && pt.Name == "InfoPage");
    }

    [Fact]
    public async Task RoundTripMarkdownContent_WhenAnInfoPageIsSaved_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Event @event = await _factory.CreateEventAsync();

        db.InfoPages.Add(InfoPage.Create(@event.Id, "Packing List", "# Bring a jacket"));
        await db.SaveChangesAsync();

        InfoPage infoPage = await db.InfoPages.AsNoTracking().SingleAsync(p => p.EventId == @event.Id);
        Assert.Equal("# Bring a jacket", infoPage.MarkdownContent);
    }

    [Fact]
    public async Task ExposePageTypeId_WhenQueryingTheBasePagesTable_WithoutJoiningInfoPages()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Event @event = await _factory.CreateEventAsync();
        db.InfoPages.Add(InfoPage.Create(@event.Id, "About", "Welcome!"));
        await db.SaveChangesAsync();

        Page page = await db.Pages.AsNoTracking().SingleAsync(p => p.EventId == @event.Id);

        Assert.Equal(PageTypeIds.InfoPage, page.PageTypeId);
    }

    [Fact]
    public async Task CascadeDeletePages_WhenTheUnderlyingEventIsDeleted_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Event @event = await _factory.CreateEventAsync();
        db.InfoPages.Add(InfoPage.Create(@event.Id, "FAQ", "Q: ... A: ..."));
        await db.SaveChangesAsync();

        db.Events.Remove(@event);
        await db.SaveChangesAsync();

        Assert.False(await db.Pages.AnyAsync(p => p.EventId == @event.Id));
        Assert.False(await db.InfoPages.AnyAsync(p => p.EventId == @event.Id));
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheReferencedPageTypeIsDeleted_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        using (IServiceScope setupScope = _factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
            setupDb.InfoPages.Add(InfoPage.Create(@event.Id, "About", "Welcome!"));
            await setupDb.SaveChangesAsync();
        }

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.PageTypes.Remove(await db.PageTypes.SingleAsync(pt => pt.Id == PageTypeIds.InfoPage));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTitleIsAllWhitespace_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Event @event = await _factory.CreateEventAsync();

        db.InfoPages.Add(InfoPage.Create(@event.Id, "   ", "content"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
