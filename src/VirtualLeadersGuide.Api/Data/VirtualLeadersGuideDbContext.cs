using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Data;

// IdentityDbContext<ApplicationUser> adds the AspNetUsers/AspNetRoles/... tables (string keys, IdentityUser's
// default shape). Role/UserRole below are a different, app-owned concept - see ADR-0017/ADR-0024 and
// CONTEXT.md's User entry. ApplicationUser is itself the person (ADR-0024); Role/UserRole stay plain POCOs,
// invisible to JsonApiDotNetCore, same as AspNetRoles/AspNetUserRoles - see
// IdentityEntitiesAreNotJsonApiResourcesShould and DomainAuthorizationEntitiesAreNotJsonApiResourcesShould.
public class VirtualLeadersGuideDbContext(DbContextOptions<VirtualLeadersGuideDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SmokeTestEntity> SmokeTestEntities => Set<SmokeTestEntity>();

    // "Roles"/"UserRoles" as property names collide with IdentityDbContext<ApplicationUser>'s own Roles/
    // UserRoles (AspNetRoles/AspNetUserRoles) - Domain-prefixed here to disambiguate on the C# side only;
    // the underlying SQL table names (set below) are the plain "Roles"/"UserRoles" CONTEXT.md's language
    // uses, since the real AspNet* table names don't collide with them.
    public DbSet<Role> DomainRoles => Set<Role>();

    public DbSet<UserRole> DomainUserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");
            entity.Property(r => r.Name).HasMaxLength(64);
            entity.HasIndex(r => r.Name).IsUnique();
            entity.HasData(
                new Role { Id = RoleIds.Admin, Name = RoleNames.Admin },
                new Role { Id = RoleIds.Director, Name = RoleNames.Director });
        });

        builder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");

            entity.HasOne(grant => grant.User)
                .WithMany()
                .HasForeignKey(grant => grant.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(grant => grant.Role)
                .WithMany(role => role.Grants)
                .HasForeignKey(grant => grant.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(grant => grant.EventId);

            // ADR-0017: SQL Server (and, per UserRoleSchemaShould, SQLite) treats a NULL EventId as equal
            // to another NULL under a plain unique index, so platform-wide and Event-scoped grants need
            // separate filtered indexes rather than one plain one on (UserId, RoleId, EventId).
            entity.HasIndex(grant => new { grant.UserId, grant.RoleId })
                .IsUnique()
                .HasDatabaseName("IX_UserRoles_PlatformWide")
                .HasFilter("[EventId] IS NULL");

            entity.HasIndex(grant => new { grant.UserId, grant.RoleId, grant.EventId })
                .IsUnique()
                .HasDatabaseName("IX_UserRoles_EventScoped")
                .HasFilter("[EventId] IS NOT NULL");
        });
    }
}
