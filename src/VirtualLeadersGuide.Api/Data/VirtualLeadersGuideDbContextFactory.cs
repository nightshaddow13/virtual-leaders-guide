using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace VirtualLeadersGuide.Api.Data;

// Used only by `dotnet ef migrations add`/`dotnet ef database update` at design
// time. Real runtime wiring goes through Program.cs -> AddSqlServerDbContext,
// which needs an Aspire-injected connection string that isn't present when
// running EF tooling directly. This connection string is never used at runtime.
public class VirtualLeadersGuideDbContextFactory : IDesignTimeDbContextFactory<VirtualLeadersGuideDbContext>
{
    public VirtualLeadersGuideDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VirtualLeadersGuideDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=VirtualLeadersGuide;Trusted_Connection=True;TrustServerCertificate=True;");

        // P2-6, #15: Event.Passcode's converter (VirtualLeadersGuideDbContext.BuildPasscodeConverter) resolves
        // IDataProtectionProvider from the application service provider at model-build time, and throws if
        // there isn't one - which `dotnet ef migrations add` would otherwise hit, since there's no real host
        // here to have called AddDataProtection(). An ephemeral, keys-never-touch-disk provider satisfies
        // model building only; it's never used to protect real data (design-time tooling never runs
        // SaveChanges against real rows).
        optionsBuilder.UseApplicationServiceProvider(
            new ServiceCollection().AddDataProtection().Services.BuildServiceProvider());

        return new VirtualLeadersGuideDbContext(optionsBuilder.Options);
    }
}
