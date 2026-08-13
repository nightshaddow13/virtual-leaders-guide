using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    // No "Domain" prefix needed - nothing on IdentityDbContext<ApplicationUser> already uses "Events".
    /// <summary>Every <see cref="Event"/> row.</summary>
    public DbSet<Event> Events => Set<Event>();

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

            // P2-6, #15: real FK against Event now that Event exists. Cascade so deleting an Event takes its
            // Director grants with it - provisional, since no ticket builds Event deletion yet; see the
            // rationale comment on UserRole.EventId.
            entity.HasOne(grant => grant.Event)
                .WithMany(@event => @event.RoleGrants)
                .HasForeignKey(grant => grant.EventId)
                .OnDelete(DeleteBehavior.Cascade);

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

        builder.Entity<Event>(entity =>
        {
            // Both CHECK constraints in one ToTable call - calling ToTable repeatedly on the same entity risks
            // later calls clobbering earlier table-level configuration.
            entity.ToTable("Events", table =>
            {
                // Name.Trim() happens in the setter (Event.cs), but this is the backstop for anything that
                // writes the column outside that setter (raw SQL, a future admin tool, etc.). TRIM(), not
                // LEN(TRIM())> 0 - SQL Server has no LENGTH() and SQLite has no LEN(), so a length comparison
                // can't be written portably; a direct empty-string comparison avoids needing either (ADR-0014:
                // DAC tests build this schema on SQLite, not by replaying the real SQL Server migration, so it
                // has to actually parse there too).
                table.HasCheckConstraint("CK_Events_Name_NotEmpty", "TRIM(Name) <> ''");

                // Backstops Slug's URL-safety beyond what SlugDerivation.From's callers might produce or an
                // Admin might hand-type: lowercase alphanumerics with single internal hyphens only, no
                // leading/trailing hyphen, non-empty. Built from LIKE with only '%' wildcards plus a
                // REPLACE-chain, not a bracket character class (SQL Server's LIKE '[^a-z0-9-]' isn't recognized
                // as a character class by SQLite's LIKE at all - and SQLite's GLOB equivalent isn't recognized
                // by SQL Server - so neither engine's native syntax is usable here). Verbose, but every piece is
                // standard SQL both engines execute identically. (Slug's own setter already forces lowercase,
                // so this only ever needs to guard characters/hyphen placement, not case.)
                table.HasCheckConstraint("CK_Events_Slug_Format", BuildSlugFormatCheckSql());
            });

            entity.Property(e => e.Name).HasMaxLength(200);

            // Plain unique index - see this file's Event.cs rationale comment for why that's correct at this
            // phase (not a filtered/archiving-aware index; out of scope for P2-6).
            entity.HasIndex(e => e.Name).IsUnique();

            entity.Property(e => e.Slug).HasMaxLength(100);
            entity.HasIndex(e => e.Slug).IsUnique();

            // Passcode is ciphertext once this converter runs - no DB constraint can validate its plaintext
            // shape (see DataProtectionStringConverter's remarks). PasscodeGenerator is what upholds "never
            // blank" instead, at the point a caller assigns it.
            entity.Property(e => e.Passcode).HasConversion(BuildPasscodeConverter());
        });
    }

    // Strips every allowed Slug character via a chained REPLACE and asserts nothing remains - see the
    // rationale on CK_Events_Slug_Format above for why this (rather than a character-class LIKE/GLOB pattern)
    // is what's actually portable between SQL Server and SQLite.
    private static string BuildSlugFormatCheckSql()
    {
        const string allowedCharacters = "abcdefghijklmnopqrstuvwxyz0123456789-";
        string stripped = allowedCharacters.Aggregate("Slug", (sql, c) => $"REPLACE({sql}, '{c}', '')");

        return "Slug <> '' AND Slug NOT LIKE '-%' AND Slug NOT LIKE '%-' AND Slug NOT LIKE '%--%' " +
            $"AND {stripped} = ''";
    }

    // Event.Passcode's IDataProtector can't be constructor-injected: AddSqlServerDbContext registers this
    // context via AddDbContextPool, and a pooled context's constructor may only take DbContextOptions<T> - a
    // second constructor parameter breaks pooling. Instead, resolve it from the application's own DI
    // container via CoreOptionsExtension.ApplicationServiceProvider, which AddDbContext/AddDbContextPool
    // already populate automatically. Throws rather than falling back to plaintext if AddDataProtection() was
    // never called - Event.Passcode's whole reason for existing (ADR-0009) is that it's never stored in the
    // clear, so failing closed here is deliberate, matching InternalApiKeyAuthenticationHandler's posture for
    // a missing key elsewhere in this project.
    private DataProtectionStringConverter BuildPasscodeConverter()
    {
        if (GetApplicationServiceProvider()?.GetService(typeof(IDataProtectionProvider))
            is not IDataProtectionProvider provider)
        {
            throw new InvalidOperationException(
                "Event.Passcode requires an IDataProtectionProvider - the host must call AddDataProtection() " +
                "(and, for design-time tooling, UseApplicationServiceProvider) before this model is built.");
        }

        return new DataProtectionStringConverter(provider.CreateProtector("VirtualLeadersGuide.Event.Passcode"));
    }

    // Isolated behind its own method rather than inlined into BuildPasscodeConverter - this reach into EF
    // Core's internal CoreOptionsExtension is inherently a bit fragile (see BuildPasscodeConverter's comment
    // for why a pooled context forces this route instead of constructor injection), so keeping it to one
    // named, documented spot means a future EF Core version that changes this internal path only needs
    // updating here.
    private IServiceProvider? GetApplicationServiceProvider() =>
        this.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>()
            .FirstOrDefault()?.ApplicationServiceProvider;
}
