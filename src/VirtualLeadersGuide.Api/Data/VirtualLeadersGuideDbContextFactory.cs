using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

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

        return new VirtualLeadersGuideDbContext(optionsBuilder.Options);
    }
}
