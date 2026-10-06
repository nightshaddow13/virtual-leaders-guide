using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// EF-model-level coverage of P8-2's (#166) schema: <c>Facilities</c>/<c>FacilityTypes</c>' check
/// constraints, the <c>Facilities</c>→<c>FacilityTypes</c> foreign key's Restrict behavior (ADR-0071 - a
/// <see cref="FacilityType"/> is never reaped, so nothing should be able to delete one out from under a
/// referencing <see cref="Facility"/>), and <c>FacilityTypes</c>' unique index on <c>Name</c> - exercised
/// directly against the DbContext rather than through HTTP, same pattern as <see cref="PageSchemaShould"/>.
/// </remarks>
public class FacilitySchemaShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task RoundTripNameAndFacilityTypeId_WhenAFacilityIsSaved_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        FacilityType facilityType = FacilityType.Create("Camp");
        db.FacilityTypes.Add(facilityType);
        await db.SaveChangesAsync();

        db.Facilities.Add(Facility.Create("Camp Blackhawk", facilityType.Id));
        await db.SaveChangesAsync();

        Facility facility = await db.Facilities.AsNoTracking().SingleAsync(f => f.FacilityTypeId == facilityType.Id);
        Assert.Equal("Camp Blackhawk", facility.Name);
        Assert.Equal(facilityType.Id, facility.FacilityTypeId);
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenFacilityNameIsAllWhitespace_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        FacilityType facilityType = FacilityType.Create("Camp");
        db.FacilityTypes.Add(facilityType);
        await db.SaveChangesAsync();

        db.Facilities.Add(Facility.Create("   ", facilityType.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenFacilityTypeNameIsAllWhitespace_ForSaveChanges()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();

        db.FacilityTypes.Add(FacilityType.Create("   "));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <remarks>
    /// Same-case duplicate only - SQLite's default collation is case-sensitive (ADR-0014), unlike SQL
    /// Server's, so a mixed-case duplicate ("CAMP" vs. "Camp") wouldn't collide at this DB-level index on
    /// the test provider even though it would in production. The case-insensitive rule is instead enforced
    /// as a pre-check in <c>FacilityTypeResourceDefinition.CheckForConflictsAsync</c>, covered by
    /// <see cref="FacilityTypesResourceShould.RejectWithConflict_WhenAdminCreatesADuplicateNameCaseInsensitively_ForPost"/> -
    /// this test is the DB-level backstop for an exact-case collision, same split
    /// <see cref="EventSchemaShould.ThrowDbUpdateException_WhenTwoEventsShareASlug_ForSaveChanges"/> uses
    /// for <c>Event.Slug</c>.
    /// </remarks>
    [Fact]
    public async Task ThrowDbUpdateException_WhenTwoFacilityTypesShareAName_ForSaveChanges()
    {
        using IServiceScope setupScope = _factory.Services.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        setupDb.FacilityTypes.Add(FacilityType.Create("Camp"));
        await setupDb.SaveChangesAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.FacilityTypes.Add(FacilityType.Create("Camp"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThrowDbUpdateException_WhenTheReferencedFacilityTypeIsDeletedWhileAFacilityStillReferencesIt_ForSaveChanges()
    {
        FacilityType facilityType;
        using (IServiceScope setupScope = _factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
            facilityType = FacilityType.Create("Camp");
            setupDb.FacilityTypes.Add(facilityType);
            setupDb.Facilities.Add(Facility.Create("Camp Blackhawk", facilityType.Id));
            await setupDb.SaveChangesAsync();
        }

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        db.FacilityTypes.Remove(await db.FacilityTypes.SingleAsync(ft => ft.Id == facilityType.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
