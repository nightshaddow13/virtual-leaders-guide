using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>Adds the app's Role/UserRole/Event tables alongside <see cref="IdentityDbContext{TUser}"/>'s own.</summary>
/// <remarks>
/// See ADR-0017/ADR-0024 and CONTEXT.md's <c>User</c> entry for why <see cref="Role"/>/<see cref="UserRole"/>
/// are a separate, app-owned concept, and why <see cref="ApplicationUser"/> — not a domain <c>User</c> row —
/// is the person. <see cref="Role"/>/<see cref="UserRole"/> stay plain POCOs, never exposed as
/// JsonApiDotNetCore resources (ADR-0017's Consequences), same as <c>AspNetRoles</c>/<c>AspNetUserRoles</c> —
/// see <c>IdentityEntitiesAreNotJsonApiResourcesShould</c> and
/// <c>DomainAuthorizationEntitiesAreNotJsonApiResourcesShould</c>.
/// </remarks>
public class VirtualLeadersGuideDbContext(DbContextOptions<VirtualLeadersGuideDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SmokeTestEntity> SmokeTestEntities => Set<SmokeTestEntity>();

    /// <remarks>
    /// "Domain"-prefixed only to disambiguate from <see cref="IdentityDbContext{TUser}"/>'s own
    /// <c>Roles</c>/<c>UserRoles</c> (<c>AspNetRoles</c>/<c>AspNetUserRoles</c>) on the C# side — the
    /// underlying SQL table names are the plain <c>Roles</c>/<c>UserRoles</c> CONTEXT.md's language uses,
    /// since the real <c>AspNet*</c> table names don't collide with them.
    /// </remarks>
    public DbSet<Role> DomainRoles => Set<Role>();

    public DbSet<UserRole> DomainUserRoles => Set<UserRole>();

    /// <summary>Every <see cref="Event"/> row.</summary>
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Role>(ConfigureRoles);
        builder.Entity<UserRole>(ConfigureUserRoles);
        builder.Entity<Event>(ConfigureEvents);
    }

    private static void ConfigureRoles(EntityTypeBuilder<Role> entity)
    {
        entity.ToTable("Roles");
        entity.Property(r => r.Name).HasMaxLength(64);
        entity.HasIndex(r => r.Name).IsUnique();
        entity.HasData(
            new Role { Id = RoleIds.Admin, Name = RoleNames.Admin },
            new Role { Id = RoleIds.Director, Name = RoleNames.Director });
    }

    /// <remarks>
    /// The grant→Event foreign key cascades — deleting an Event takes its Director grants with it —
    /// provisionally, since no ticket builds Event deletion yet; see <see cref="UserRole.EventId"/>. The
    /// platform-wide and Event-scoped grants need two filtered unique indexes rather than one plain index on
    /// (UserId, RoleId, EventId); see ADR-0017 for why a plain index can't do this.
    /// </remarks>
    private static void ConfigureUserRoles(EntityTypeBuilder<UserRole> entity)
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

        entity.HasOne(grant => grant.Event)
            .WithMany(@event => @event.RoleGrants)
            .HasForeignKey(grant => grant.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(grant => new { grant.UserId, grant.RoleId })
            .IsUnique()
            .HasDatabaseName("IX_UserRoles_PlatformWide")
            .HasFilter("[EventId] IS NULL");

        entity.HasIndex(grant => new { grant.UserId, grant.RoleId, grant.EventId })
            .IsUnique()
            .HasDatabaseName("IX_UserRoles_EventScoped")
            .HasFilter("[EventId] IS NOT NULL");
    }

    /// <remarks>
    /// <see cref="Event.Name"/>'s uniqueness index is plain, not filtered/archiving-aware — see the
    /// <c>Name</c> remarks on <c>Event.cs</c> for why that's correct at this phase (out of scope for P2-6,
    /// #15). <see cref="Event.Passcode"/> gets no CHECK constraint for its shape: it's ciphertext once
    /// <see cref="BuildPasscodeConverter"/> runs, so no DB constraint could validate a plaintext shape anyway
    /// — <c>PasscodeGenerator</c> upholds "never blank" instead, at the point a caller assigns it.
    /// </remarks>
    private void ConfigureEvents(EntityTypeBuilder<Event> entity)
    {
        entity.ToTable("Events", ConfigureEventCheckConstraints);

        entity.Property(e => e.Name).HasMaxLength(200);
        entity.HasIndex(e => e.Name).IsUnique();

        entity.Property(e => e.Slug).HasMaxLength(100);
        entity.HasIndex(e => e.Slug).IsUnique();

        entity.Property(e => e.Passcode).HasConversion(BuildPasscodeConverter());
    }

    /// <remarks>
    /// Both constraints in one call — calling <c>ToTable</c> repeatedly on the same entity risks a later
    /// call clobbering earlier table-level configuration.
    /// </remarks>
    private static void ConfigureEventCheckConstraints(TableBuilder<Event> table)
    {
        table.HasCheckConstraint("CK_Events_Name_NotEmpty", BuildNameNotEmptyCheckSql());
        table.HasCheckConstraint("CK_Events_Slug_Format", BuildSlugFormatCheckSql());
    }

    /// <remarks>
    /// <see cref="Event.Name"/>'s setter already trims (<c>Event.cs</c>); this is the backstop for anything
    /// that writes the column outside that setter (raw SQL, a future admin tool, etc.). Uses
    /// <c>TRIM(Name) &lt;&gt; ''</c> rather than a length check because SQL Server has no <c>LENGTH()</c>
    /// and SQLite has no <c>LEN()</c>, so a length comparison can't be written portably — ADR-0014 requires
    /// this schema to also build on SQLite, not just SQL Server.
    /// </remarks>
    private static string BuildNameNotEmptyCheckSql() => "TRIM(Name) <> ''";

    /// <remarks>
    /// Backstops <see cref="Event.Slug"/>'s URL-safety beyond what <c>SlugDerivation.From</c>'s callers
    /// might produce or an Admin might hand-type: lowercase alphanumerics with single internal hyphens only,
    /// no leading/trailing hyphen, non-empty. Built from <c>LIKE</c> with only <c>%</c> wildcards plus a
    /// <c>REPLACE</c> chain, not a bracket character class, because neither engine's native syntax is
    /// portable — SQL Server's <c>LIKE '[^a-z0-9-]'</c> isn't recognized as a character class by SQLite's
    /// <c>LIKE</c> at all, and SQLite's <c>GLOB</c> equivalent isn't recognized by SQL Server. Verbose, but
    /// every piece is standard SQL both engines execute identically. (<see cref="Event.Slug"/>'s setter
    /// already forces lowercase, so this only ever needs to guard characters/hyphen placement, not case.)
    /// </remarks>
    private static string BuildSlugFormatCheckSql()
    {
        const string allowedCharacters = "abcdefghijklmnopqrstuvwxyz0123456789-";
        string stripped = allowedCharacters.Aggregate("Slug", (sql, c) => $"REPLACE({sql}, '{c}', '')");

        return "Slug <> '' AND Slug NOT LIKE '-%' AND Slug NOT LIKE '%-' AND Slug NOT LIKE '%--%' " +
            $"AND {stripped} = ''";
    }

    /// <remarks>
    /// <see cref="Event.Passcode"/>'s <see cref="IDataProtector"/> can't be constructor-injected:
    /// <c>AddSqlServerDbContext</c> registers this context via <c>AddDbContextPool</c>, and a pooled
    /// context's constructor may only take <see cref="DbContextOptions{TContext}"/> — a second constructor
    /// parameter breaks pooling. Resolved from the application's own DI container via
    /// <see cref="CoreOptionsExtension.ApplicationServiceProvider"/> instead, which
    /// <c>AddDbContext</c>/<c>AddDbContextPool</c> already populate automatically. Throws rather than
    /// falling back to plaintext if <c>AddDataProtection()</c> was never called — <see cref="Event.Passcode"/>'s
    /// whole reason for existing (ADR-0009) is that it's never stored in the clear, so failing closed here is
    /// deliberate, matching <c>InternalApiKeyAuthenticationHandler</c>'s posture for a missing key elsewhere
    /// in this project.
    /// </remarks>
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

    /// <remarks>
    /// Isolated behind its own method rather than inlined into <see cref="BuildPasscodeConverter"/> — this
    /// reach into EF Core's internal <see cref="CoreOptionsExtension"/> is inherently a bit fragile, so
    /// keeping it to one named, documented spot means a future EF Core version that changes this internal
    /// path only needs updating here.
    /// </remarks>
    private IServiceProvider? GetApplicationServiceProvider() =>
        this.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>()
            .FirstOrDefault()?.ApplicationServiceProvider;
}
