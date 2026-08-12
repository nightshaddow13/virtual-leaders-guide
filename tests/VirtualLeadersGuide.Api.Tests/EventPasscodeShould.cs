using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

// Proves ADR-0009/ADR-0026's actual claim - Event.Passcode is encrypted at rest, not just "some converter
// exists" - by reading the raw column via ADO.NET and asserting it's not the plaintext, then reading it back
// through a fresh DbContext (forcing an actual decrypt, not just a change-tracker cache hit) and asserting the
// plaintext round-trips.
public class EventPasscodeShould : IAsyncLifetime
{
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task RoundTripPlaintext_WhenReadBackThroughAFreshContext_ForSaveChanges()
    {
        const string passcode = "TigerLantern";
        var eventId = Guid.NewGuid();

        using (IServiceScope writeScope = _factory.Services.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
            db.Events.Add(new Event
            {
                Id = eventId, Name = "Fall Retreat", Slug = "fall-retreat", Passcode = passcode
            });
            await db.SaveChangesAsync();
        }

        // A fresh scope/context, not the one that wrote the row - proves this went through a real decrypt,
        // not a change-tracker cache still holding the plaintext object that was Add()-ed above.
        using IServiceScope readScope = _factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        Event reloaded = await readDb.Events.AsNoTracking().SingleAsync(e => e.Id == eventId);

        Assert.Equal(passcode, reloaded.Passcode);
    }

    [Fact]
    public async Task StoreCiphertext_NotPlaintext_ForSaveChanges()
    {
        const string passcode = "TigerLantern";
        var eventId = Guid.NewGuid();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
            db.Events.Add(new Event
            {
                Id = eventId, Name = "Fall Retreat", Slug = "fall-retreat", Passcode = passcode
            });
            await db.SaveChangesAsync();
        }

        // Bypasses EF entirely - reads the raw stored column value via the same underlying SQLite connection
        // ApiWebApplicationFactory hands every DbContext instance in this test run. No WHERE clause needed:
        // this factory's in-memory database is isolated per test and this test wrote exactly one Event.
        using IServiceScope rawScope = _factory.Services.CreateScope();
        var rawDb = rawScope.ServiceProvider.GetRequiredService<VirtualLeadersGuideDbContext>();
        var connection = (SqliteConnection)rawDb.Database.GetDbConnection();
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Passcode FROM Events";
        var rawValue = (string?)await command.ExecuteScalarAsync();

        Assert.NotNull(rawValue);
        Assert.NotEqual(passcode, rawValue);
    }
}
