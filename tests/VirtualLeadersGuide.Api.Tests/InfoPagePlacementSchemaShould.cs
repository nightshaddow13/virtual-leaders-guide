using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of the <c>InfoPagePlacements</c> schema (P5-24, #208): one Placement per InfoPage,
/// one InfoPage per screen, the InfoPage cascade and the Restrict on its Tier foreign keys.
/// </remarks>
public class InfoPagePlacementSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;
    private Event _event = null!;
    private Tab _tab = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
        _event = await _factory.CreateEventAsync();
        _tab = await _factory.CreateTabAsync(_event.Id);
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task ThrowDbUpdateException_WhenAnInfoPageIsPlacedTwice_ForSaveChanges()
    {
        InfoPage page = await _factory.CreateInfoPageAsync(_event.Id);
        Tab other = await _factory.CreateTabAsync(_event.Id);
        await _factory.CreateInfoPagePlacementAsync(_event.Id, page.Id, _tab.Id);

        await Assert.ThrowsAsync<DbUpdateException>(() => _factory.CreateInfoPagePlacementAsync(_event.Id, page.Id, other.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowDbUpdateException_WhenASecondInfoPageTakesTheSameScreen_ForSaveChanges(bool useSubTab)
    {
        InfoPage first = await _factory.CreateInfoPageAsync(_event.Id);
        InfoPage second = await _factory.CreateInfoPageAsync(_event.Id);
        Guid? subTabId = useSubTab ? (await _factory.CreateSubTabAsync(_event.Id, _tab.Id)).Id : null;
        await _factory.CreateInfoPagePlacementAsync(_event.Id, first.Id, _tab.Id, subTabId);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _factory.CreateInfoPagePlacementAsync(_event.Id, second.Id, _tab.Id, subTabId));
    }

    [Fact]
    public async Task AllowTheBareTabAndItsSubTabToHoldDifferentInfoPages_ForSaveChanges()
    {
        InfoPage first = await _factory.CreateInfoPageAsync(_event.Id);
        InfoPage second = await _factory.CreateInfoPageAsync(_event.Id);
        SubTab subTab = await _factory.CreateSubTabAsync(_event.Id, _tab.Id);

        await _factory.CreateInfoPagePlacementAsync(_event.Id, first.Id, _tab.Id);
        await _factory.CreateInfoPagePlacementAsync(_event.Id, second.Id, _tab.Id, subTab.Id);
    }

    [Fact]
    public async Task CascadeThePlacement_WhenItsInfoPageIsDeleted_ForSaveChanges()
    {
        InfoPage page = await _factory.CreateInfoPageAsync(_event.Id);
        await _factory.CreateInfoPagePlacementAsync(_event.Id, page.Id, _tab.Id);
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        db.InfoPages.Remove(await db.InfoPages.SingleAsync(p => p.Id == page.Id));
        await db.SaveChangesAsync();

        Assert.False(await db.InfoPagePlacements.AnyAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenATabIsDeletedWhileAPlacementReferencesIt_ForSaveChanges()
    {
        InfoPage page = await _factory.CreateInfoPageAsync(_event.Id);
        await _factory.CreateInfoPagePlacementAsync(_event.Id, page.Id, _tab.Id);
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        db.Tabs.Remove(await db.Tabs.SingleAsync(t => t.Id == _tab.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
