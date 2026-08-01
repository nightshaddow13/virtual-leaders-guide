using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VirtualLeadersGuide.Api.Data;

// IdentityDbContext<ApplicationUser> adds the AspNetUsers/AspNetRoles/... tables (string keys, IdentityUser's
// default shape - no domain Roles table collision, since P2-3's Role/UserRole tables are a different,
// app-owned concept entirely; see ADR-0022 and CONTEXT.md's Credential entry). Those Identity entities don't
// implement IIdentifiable, so JsonApiDotNetCore never exposes them under /api - see
// IdentityEntitiesAreNotJsonApiResourcesShould.
public class VirtualLeadersGuideDbContext(DbContextOptions<VirtualLeadersGuideDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SmokeTestEntity> SmokeTestEntities => Set<SmokeTestEntity>();
}
