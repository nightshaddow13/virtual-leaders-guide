using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P5-11's (#96) <c>SubTabs</c> schema: the Name check constraint and the
/// <c>SubTabs</c>→<c>Tabs</c> Restrict (a Tab is reaped only
/// once nothing references it, ADR-0046) - exercised directly against the DbContext.
/// </remarks>
public class SubTabSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task RoundTripEventIdTabIdNameAndSortOrder_WhenASubTabIsSaved_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);

        SubTab subTab = await _factory.CreateSubTabAsync(@event.Id, tab.Id, "Round Robin", sortOrder: 1);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        SubTab reloaded = await db.SubTabs.AsNoTracking().SingleAsync(st => st.Id == subTab.Id);
        Assert.Equal(@event.Id, reloaded.EventId);
        Assert.Equal(tab.Id, reloaded.TabId);
        Assert.Equal("Round Robin", reloaded.Name);
        Assert.Equal(1, reloaded.SortOrder);
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenSubTabNameIsAllWhitespace_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.SubTabs.Add(SubTab.Create(@event.Id, tab.Id, "   ", 0));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheReferencedTabIsDeletedWhileASubTabStillReferencesIt_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        await _factory.CreateSubTabAsync(@event.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Tabs.Remove(await db.Tabs.SingleAsync(t => t.Id == tab.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
