using Microsoft.EntityFrameworkCore;

namespace VirtualLeadersGuide.Api.Data;

public class VirtualLeadersGuideDbContext(DbContextOptions<VirtualLeadersGuideDbContext> options)
    : DbContext(options)
{
    public DbSet<SmokeTestEntity> SmokeTestEntities => Set<SmokeTestEntity>();
}
