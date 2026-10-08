using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P5-11's (#96) <c>SubSections</c> schema: the Name check constraint and the
/// <c>SubSections</c>→<c>Sections</c> Restrict - exercised directly against the DbContext.
/// </remarks>
public class SubSectionSchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task RoundTripSectionIdNameAndSortOrder_WhenASubSectionIsSaved_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        Section section = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id);

        SubSection subSection = await _factory.CreateSubSectionAsync(@event.Id, section.Id, "Canoeing", sortOrder: 1);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        SubSection reloaded = await db.SubSections.AsNoTracking().SingleAsync(ss => ss.Id == subSection.Id);
        Assert.Equal(section.Id, reloaded.SectionId);
        Assert.Equal("Canoeing", reloaded.Name);
        Assert.Equal(1, reloaded.SortOrder);
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenSubSectionNameIsAllWhitespace_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        Section section = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.SubSections.Add(SubSection.Create(@event.Id, section.Id, "   ", 0));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheReferencedSectionIsDeletedWhileASubSectionStillReferencesIt_ForSaveChanges()
    {
        Event @event = await _factory.CreateEventAsync();
        Tab tab = await _factory.CreateTabAsync(@event.Id);
        Section section = await _factory.CreateSectionUnderTabAsync(@event.Id, tab.Id);
        await _factory.CreateSubSectionAsync(@event.Id, section.Id);

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.Sections.Remove(await db.Sections.SingleAsync(s => s.Id == section.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
