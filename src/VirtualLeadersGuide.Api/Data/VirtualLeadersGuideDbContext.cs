using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>Adds the app's Role/UserRole/Event tables alongside <see cref="IdentityDbContext{TUser}"/>'s own.</summary>
/// <remarks>
/// See ADR-0017/ADR-0024 and CONTEXT.md's <c>User</c> entry for why <see cref="Role"/>/<see cref="UserRole"/>
/// are a separate, app-owned concept, and why <see cref="ApplicationUser"/> — not a domain <c>User</c> row —
/// is the person. <see cref="Role"/> stays a plain POCO, never exposed as a JsonApiDotNetCore resource
/// (ADR-0017's Consequences), same as <c>AspNetRoles</c> — see <c>IdentityEntitiesAreNotJsonApiResourcesShould</c>
/// and <c>DomainAuthorizationEntitiesAreNotJsonApiResourcesShould</c>. <see cref="UserRole"/> is exposed,
/// Admin-only, at <c>/api/roleGrants</c> (P2-8, #17; ADR-0033) — see <c>UserRoleResourceDefinition</c>.
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
    /// The grant→Event foreign key cascades — deleting an Event takes its Director grants with it — a
    /// considered decision (ADR-0044), not an inherited default; see <see cref="UserRole.EventId"/>. The
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
    /// <see cref="Event.Name"/> carries no unique index at all - see its remarks on <c>Event.cs</c> and
    /// ADR-0053 for why that rule lives entirely in <see cref="EventResourceDefinition.CheckForConflictsAsync"/>
    /// instead. <see cref="Event.Status"/> stores as <c>string</c> (readable in the DB and in
    /// <c>CK_Events_Status_Allowed</c> below, and matches the JSON:API wire shape - no persisted-enum
    /// precedent existed before this column, so this sets it); its default lives on <see cref="Event.Status"/>'s
    /// own property initializer, not here - see that property's remarks for why. <see cref="Event.Passcode"/>
    /// gets no CHECK constraint for its shape: it's ciphertext once <see cref="BuildPasscodeConverter"/> runs,
    /// so no DB constraint could validate a plaintext shape anyway — <c>PasscodeGenerator</c> upholds "never
    /// blank" instead, at the point a caller assigns it. <see cref="Event.Slug"/> and <see cref="Event.Passcode"/>
    /// are both explicitly <c>IsRequired()</c> here rather than left to convention, since both are typed
    /// <c>string?</c> at the C# level (see their remarks on <c>Event.cs</c> for why) - the column stays
    /// <c>NOT NULL</c> regardless. Passcode's converter is cast to the non-generic <see cref="ValueConverter"/>
    /// overload because <see cref="DataProtectionStringConverter"/> is <c>ValueConverter&lt;string, string&gt;</c>,
    /// not exactly nullability-compatible with a <c>string?</c> property for the generic overload, even though
    /// the converter never actually receives a null at runtime (the same <c>NOT NULL</c> constraint guarantees
    /// that).
    /// </remarks>
    private void ConfigureEvents(EntityTypeBuilder<Event> entity)
    {
        entity.ToTable("Events", ConfigureEventCheckConstraints);

        entity.Property(e => e.Name).HasMaxLength(200);

        entity.Property(e => e.Slug).HasMaxLength(100).IsRequired();
        entity.HasIndex(e => e.Slug).IsUnique();

        entity.Property(e => e.Passcode).HasConversion((ValueConverter)BuildPasscodeConverter()).IsRequired();

        entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
    }

    /// <remarks>
    /// Both constraints in one call — calling <c>ToTable</c> repeatedly on the same entity risks a later
    /// call clobbering earlier table-level configuration.
    /// </remarks>
    private static void ConfigureEventCheckConstraints(TableBuilder<Event> table)
    {
        table.HasCheckConstraint("CK_Events_Name_NotEmpty", BuildNameNotEmptyCheckSql());
        table.HasCheckConstraint("CK_Events_Slug_Format", BuildSlugFormatCheckSql());
        table.HasCheckConstraint("CK_Events_Dates_Ordered", BuildDatesOrderedCheckSql());
        table.HasCheckConstraint("CK_Events_Status_Allowed", BuildStatusAllowedCheckSql());
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
    /// Encodes both <see cref="Event.StartsAt"/>/<see cref="Event.EndsAt"/> rules at once: <c>EndsAt</c> may
    /// only be set once <c>StartsAt</c> already is, and must be strictly after it. Bare unquoted column names
    /// and plain comparison operators only, so it parses identically on SQL Server and SQLite (ADR-0014) -
    /// matching <see cref="BuildNameNotEmptyCheckSql"/>/<see cref="BuildSlugFormatCheckSql"/>'s portability
    /// bar. Correct on SQLite only because both columns are always stored as UTC (see
    /// <see cref="Event.StartsAt"/>'s normalizing setter) - EF's SQLite provider stores
    /// <see cref="DateTimeOffset"/> as TEXT, so comparing two values with different offsets would compare
    /// lexicographically and could give the wrong answer; an all-UTC column never has that problem.
    /// </remarks>
    private static string BuildDatesOrderedCheckSql() =>
        "EndsAt IS NULL OR (StartsAt IS NOT NULL AND EndsAt > StartsAt)";

    /// <remarks>
    /// The database-level backstop making "<see cref="EventStatus.Past"/> is never stored" an invariant, not
    /// just a convention - see <see cref="Event.Status"/>'s remarks and ADR-0053. Bare unquoted column name
    /// and string literals only, matching the portability bar the other constraint builders on this type set
    /// (ADR-0014).
    /// </remarks>
    private static string BuildStatusAllowedCheckSql() => "Status IN ('Draft', 'Live', 'Cancelled')";

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
