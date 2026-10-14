using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P5-11's (#96) <c>Tabs</c> schema: the Name check constraint -
/// exercised directly against the
/// DbContext, same pattern as <see cref="FacilitySchemaShould"/>.
/// </remarks>
public class TabSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task RoundTripEventIdNameAndSortOrder_WhenATabIsSaved_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();

        Tab tab = await _factory.CreateTabAsync(@event.Id, "Morning", sortOrder: 2);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Tab reloaded = await db.Tabs.AsNoTracking().SingleAsync(t => t.Id == tab.Id);
        Assert.Equal(@event.Id, reloaded.EventId);
        Assert.Equal("Morning", reloaded.Name);
        Assert.Equal(2, reloaded.SortOrder);
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTabNameIsAllWhitespace_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Tabs.Add(Tab.Create(@event.Id, "   ", 0));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
