using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>/<c>dotnet ef database update</c> at design time. Real
/// runtime wiring goes through <c>Program.cs</c>'s <c>AddSqlServerDbContext</c>, which needs an
/// Aspire-injected connection string that isn't present when running EF tooling directly - the connection
/// string below is never used at runtime.
/// </summary>
public class VirtualLeadersGuideDbContextFactory : IDesignTimeDbContextFactory<VirtualLeadersGuideDbContext>
{
    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="Event.Passcode"/>'s converter (<see cref="VirtualLeadersGuideDbContext.BuildPasscodeConverter"/>)
    /// resolves <see cref="IDataProtectionProvider"/> from the application service provider at model-build
    /// time, and throws if there isn't one - which <c>dotnet ef migrations add</c> would otherwise hit,
    /// since there's no real host here to have called <c>AddDataProtection()</c>. The ephemeral,
    /// keys-never-touch-disk provider below satisfies model building only; it's never used to protect real
    /// data (design-time tooling never runs <c>SaveChanges</c> against real rows).
    /// </remarks>
    public VirtualLeadersGuideDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VirtualLeadersGuideDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=VirtualLeadersGuide;Trusted_Connection=True;TrustServerCertificate=True;");

        optionsBuilder.UseApplicationServiceProvider(
            new ServiceCollection().AddDataProtection().Services.BuildServiceProvider());

        return new VirtualLeadersGuideDbContext(optionsBuilder.Options);
    }
}
