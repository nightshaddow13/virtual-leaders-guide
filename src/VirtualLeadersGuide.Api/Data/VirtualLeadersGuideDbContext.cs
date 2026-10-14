using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Adds the app's Role/UserRole/Event/Page/InfoPage/PageType tables alongside
/// <see cref="IdentityDbContext{TUser}"/>'s own.
/// </summary>
/// <remarks>
/// See ADR-0017/ADR-0024 and CONTEXT.md's <c>User</c> entry for why <see cref="Role"/>/<see cref="UserRole"/>
/// are a separate, app-owned concept, and why <see cref="ApplicationUser"/> — not a domain <c>User</c> row —
/// is the person. <see cref="Role"/> stays a plain POCO, never exposed as a JsonApiDotNetCore resource
/// (ADR-0017's Consequences), same as <c>AspNetRoles</c> — see <c>IdentityEntitiesAreNotJsonApiResourcesShould</c>
/// and <c>DomainAuthorizationEntitiesAreNotJsonApiResourcesShould</c>. <see cref="UserRole"/> is exposed,
/// Admin-only, at <c>/api/roleGrants</c> (P2-8, #17; ADR-0033) — see <c>UserRoleResourceDefinition</c>.
/// </remarks>
/// <remarks>
/// <see cref="Page"/>/<see cref="InfoPage"/>/<see cref="PageType"/> (P5-15, #20) are mapped Table-Per-Type -
/// see ADR-0055. None of the three is <c>Identifiable&lt;Guid&gt;</c> yet, so none is exposed as a
/// JsonApiDotNetCore resource regardless of the missing <c>[Resource]</c> attribute (see <see cref="Page"/>'s
/// remarks for why that distinction matters) - see <c>PageEntitiesAreNotJsonApiResourcesShould</c>. P5-16
/// (#21) is what turns <see cref="InfoPage"/> into a resource.
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

    /// <remarks>
    /// <see cref="PageType"/> stays a plain POCO, never exposed as a JsonApiDotNetCore resource, same posture
    /// as <see cref="Role"/> - see <c>PageEntitiesAreNotJsonApiResourcesShould</c>.
    /// </remarks>
    public DbSet<PageType> PageTypes => Set<PageType>();

    /// <summary>Every <see cref="Page"/> row (base table - every row also has a matching subtype row).</summary>
    public DbSet<Page> Pages => Set<Page>();

    public DbSet<InfoPage> InfoPages => Set<InfoPage>();

    /// <summary>Every <see cref="Activity"/> row (P5-6, #87).</summary>
    public DbSet<Activity> Activities => Set<Activity>();

    /// <summary>Every <see cref="Data.FacilityType"/> row (P8-2, #166).</summary>
    public DbSet<FacilityType> FacilityTypes => Set<FacilityType>();

    /// <summary>Every <see cref="Facility"/> row (P8-2, #166).</summary>
    public DbSet<Facility> Facilities => Set<Facility>();

    /// <summary>Every <see cref="Data.Tab"/> row (P5-11, #96).</summary>
    public DbSet<Tab> Tabs => Set<Tab>();

    /// <summary>Every <see cref="Data.SubTab"/> row (P5-11, #96).</summary>
    public DbSet<SubTab> SubTabs => Set<SubTab>();

    /// <summary>Every <see cref="Data.Section"/> row (P5-11, #96).</summary>
    public DbSet<Section> Sections => Set<Section>();

    /// <summary>Every <see cref="Data.SubSection"/> row (P5-11, #96).</summary>
    public DbSet<SubSection> SubSections => Set<SubSection>();

    /// <summary>Every <see cref="ActivityPlacement"/> row (P5-11, #96; ADR-0050).</summary>
    public DbSet<ActivityPlacement> ActivityPlacements => Set<ActivityPlacement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Role>(ConfigureRoles);
        builder.Entity<UserRole>(ConfigureUserRoles);
        builder.Entity<Event>(ConfigureEvents);
        builder.Entity<PageType>(ConfigurePageTypes);
        builder.Entity<Page>(ConfigurePages);
        builder.Entity<InfoPage>(ConfigureInfoPages);
        builder.Entity<Activity>(ConfigureActivities);
        builder.Entity<FacilityType>(ConfigureFacilityTypes);
        builder.Entity<Facility>(ConfigureFacilities);
        builder.Entity<Tab>(ConfigureTabs);
        builder.Entity<SubTab>(ConfigureSubTabs);
        builder.Entity<Section>(ConfigureSections);
        builder.Entity<SubSection>(ConfigureSubSections);
        builder.Entity<ActivityPlacement>(ConfigureActivityPlacements);
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

    private static void ConfigurePageTypes(EntityTypeBuilder<PageType> entity)
    {
        entity.ToTable("PageTypes");
        entity.Property(pt => pt.Name).HasMaxLength(64);
        entity.HasIndex(pt => pt.Name).IsUnique();
        entity.HasData(new PageType { Id = PageTypeIds.InfoPage, Name = "InfoPage" });
    }

    /// <remarks>
    /// <see cref="Microsoft.EntityFrameworkCore.RelationalEntityTypeBuilderExtensions.UseTptMappingStrategy{TEntity}"/>
    /// is explicit rather than left to convention - see ADR-0055 for why TPT over TPH. The
    /// <see cref="Page"/>→<see cref="Event"/> foreign key cascades, the same considered choice ADR-0044 made
    /// for <see cref="UserRole"/>'s grant→Event foreign key: a Page is meaningless once its Event is gone. The
    /// <see cref="Page"/>→<see cref="PageType"/> foreign key restricts, mirroring <see cref="UserRole"/>'s own
    /// grant→Role foreign key - a lookup row can't be deleted out from under live rows.
    /// </remarks>
    private static void ConfigurePages(EntityTypeBuilder<Page> entity)
    {
        entity.ToTable("Pages", ConfigurePageCheckConstraints);
        entity.UseTptMappingStrategy();

        entity.Property(p => p.Title).HasMaxLength(200);

        entity.HasOne(p => p.Event)
            .WithMany()
            .HasForeignKey(p => p.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(p => p.PageType)
            .WithMany(pt => pt.Pages)
            .HasForeignKey(p => p.PageTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(p => p.EventId);
    }

    /// <remarks>
    /// <see cref="Page.Title"/>'s setter already trims (<c>Page.cs</c>); this is the backstop for anything
    /// that writes the column outside that setter, matching <see cref="BuildNameNotEmptyCheckSql"/>'s
    /// portable <c>TRIM</c> form (ADR-0014: no <c>LEN()</c>/<c>LENGTH()</c> comparison is portable).
    /// </remarks>
    private static void ConfigurePageCheckConstraints(TableBuilder<Page> table) =>
        table.HasCheckConstraint("CK_Pages_Title_NotEmpty", "TRIM(Title) <> ''");

    /// <remarks>
    /// <see cref="InfoPage.MarkdownContent"/> gets no max length - free-form authored content
    /// (CONTEXT.md's InfoPage entry), same posture as <see cref="Event.Passcode"/> carrying no length cap.
    /// </remarks>
    private static void ConfigureInfoPages(EntityTypeBuilder<InfoPage> entity) => entity.ToTable("InfoPages");

    /// <remarks>
    /// The <see cref="Activity"/>→<see cref="Event"/> foreign key cascades, the same considered choice
    /// <see cref="ConfigurePages"/> already makes for <see cref="Page"/>→<see cref="Event"/>: an Activity is
    /// meaningless once its Event is gone.
    /// </remarks>
    private static void ConfigureActivities(EntityTypeBuilder<Activity> entity)
    {
        entity.ToTable("Activities", ConfigureActivityCheckConstraints);

        entity.Property(a => a.Name).HasMaxLength(200);

        entity.HasOne(a => a.Event)
            .WithMany()
            .HasForeignKey(a => a.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(a => a.EventId);
    }

    /// <remarks>
    /// <see cref="Activity.Name"/>'s setter already trims (<c>Activity.cs</c>); this is the backstop for
    /// anything that writes the column outside that setter, matching <see cref="BuildNameNotEmptyCheckSql"/>'s
    /// portable <c>TRIM</c> form (ADR-0014: no <c>LEN()</c>/<c>LENGTH()</c> comparison is portable).
    /// <see cref="Activity.Description"/> gets no max length - free-form authored content, same posture as
    /// <see cref="InfoPage.MarkdownContent"/>.
    /// </remarks>
    private static void ConfigureActivityCheckConstraints(TableBuilder<Activity> table) =>
        table.HasCheckConstraint("CK_Activities_Name_NotEmpty", "TRIM(Name) <> ''");

    /// <remarks>
    /// Unique on <see cref="Data.FacilityType.Name"/> (case-insensitively, matching SQL Server's default
    /// collation) - see <see cref="Data.FacilityType"/>'s remarks for why. Deliberately no foreign key
    /// targeting anything, and no cascade to configure - a <see cref="Data.FacilityType"/> is a standalone
    /// lookup row (ADR-0071), unlike every Event-scoped entity this context otherwise configures.
    /// </remarks>
    private static void ConfigureFacilityTypes(EntityTypeBuilder<FacilityType> entity)
    {
        entity.ToTable("FacilityTypes", ConfigureFacilityTypeCheckConstraints);

        entity.Property(ft => ft.Name).HasMaxLength(200);

        entity.HasIndex(ft => ft.Name).IsUnique();
    }

    /// <remarks>
    /// <see cref="Data.FacilityType.Name"/>'s setter already trims (<c>FacilityType.cs</c>); this is the
    /// backstop for anything that writes the column outside that setter, matching
    /// <see cref="BuildNameNotEmptyCheckSql"/>'s portable <c>TRIM</c> form (ADR-0014: no
    /// <c>LEN()</c>/<c>LENGTH()</c> comparison is portable).
    /// </remarks>
    private static void ConfigureFacilityTypeCheckConstraints(TableBuilder<FacilityType> table) =>
        table.HasCheckConstraint("CK_FacilityTypes_Name_NotEmpty", "TRIM(Name) <> ''");

    /// <remarks>
    /// Unlike every Event-scoped entity this context configures, <see cref="Facility"/> carries no Event
    /// foreign key at all (ADR-0066) - nothing to cascade from. The <see cref="Facility"/>→<see cref="FacilityType"/>
    /// foreign key restricts rather than cascades, mirroring <see cref="ConfigurePages"/>'s
    /// <see cref="Page"/>→<see cref="PageType"/> foreign key: a lookup row can't be deleted out from under
    /// live rows that reference it (ADR-0071's "no reaping" decision - deleting a <see cref="FacilityType"/>
    /// isn't a capability this story ships at all, so this is purely a backstop).
    /// </remarks>
    private static void ConfigureFacilities(EntityTypeBuilder<Facility> entity)
    {
        entity.ToTable("Facilities", ConfigureFacilityCheckConstraints);

        entity.Property(f => f.Name).HasMaxLength(200);

        entity.HasOne(f => f.FacilityType)
            .WithMany()
            .HasForeignKey(f => f.FacilityTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(f => f.FacilityTypeId);
    }

    /// <remarks>
    /// <see cref="Facility.Name"/>'s setter already trims (<c>Facility.cs</c>); this is the backstop for
    /// anything that writes the column outside that setter, matching <see cref="BuildNameNotEmptyCheckSql"/>'s
    /// portable <c>TRIM</c> form (ADR-0014: no <c>LEN()</c>/<c>LENGTH()</c> comparison is portable).
    /// </remarks>
    private static void ConfigureFacilityCheckConstraints(TableBuilder<Facility> table) =>
        table.HasCheckConstraint("CK_Facilities_Name_NotEmpty", "TRIM(Name) <> ''");

    /// <remarks>
    /// <see cref="Tab.EventId"/> gets no FK at all, the same denormalized-column treatment as
    /// <see cref="ActivityPlacement.EventId"/> (ADR-0050) and for the same reason: a Tab's lifecycle is
    /// app-driven (ADR-0046 - created and reaped only inside <see cref="ActivityPlacementResourceDefinition"/>),
    /// never a DB cascade side effect. This isn't just a style choice - once <see cref="Section"/>'s two
    /// optional parents (<see cref="Section.ParentTabId"/>/<see cref="Section.ParentSubTabId"/>) are
    /// accounted for, a cascading <see cref="Event"/>→Tier FK on every one of
    /// <see cref="Tab"/>/<see cref="SubTab"/>/<see cref="Section"/>/<see cref="SubSection"/> is
    /// unimplementable without SQL Server's "multiple cascade paths" rejection (branching from Tab into
    /// both a direct Section parent and a SubTab-mediated one), and even a single-path attempt at
    /// cascading directly from Event while Restrict FKs point deeper into this subtree (see
    /// <see cref="ConfigureActivityPlacements"/>'s remarks) is a demonstrated SQLite ordering bug: whichever
    /// cascade branch the engine processes first can try to delete a row another, not-yet-processed branch
    /// still Restricts. <see cref="EventResourceDefinition"/>'s delete handling explicitly cleans up a
    /// deleted Event's whole Tier+Placement subtree instead, bottom-up, before the Event row itself goes.
    /// </remarks>
    private static void ConfigureTabs(EntityTypeBuilder<Tab> entity)
    {
        entity.ToTable("Tabs", ConfigureTabCheckConstraints);

        entity.Property(t => t.Name).HasMaxLength(200);

        entity.HasIndex(t => t.EventId);
    }

    /// <remarks>
    /// <see cref="Tab.Name"/>'s setter already trims (<c>Tab.cs</c>); this is the backstop for anything
    /// that writes the column outside that setter, matching <see cref="BuildNameNotEmptyCheckSql"/>'s
    /// portable <c>TRIM</c> form (ADR-0014: no <c>LEN()</c>/<c>LENGTH()</c> comparison is portable).
    /// </remarks>
    private static void ConfigureTabCheckConstraints(TableBuilder<Tab> table) =>
        table.HasCheckConstraint("CK_Tabs_Name_NotEmpty", "TRIM(Name) <> ''");

    /// <remarks>
    /// <see cref="SubTab.EventId"/> gets no FK, same reasoning as <see cref="ConfigureTabs"/>.
    /// <see cref="SubTab"/>→<see cref="Tab"/> restricts - a Tab is reaped only once nothing references it
    /// anymore (ADR-0046); restricting here backstops that invariant at the DB level, safely, since nothing
    /// else in this schema cascades into <c>SubTabs</c> or <c>Tabs</c> to create an ordering conflict (see
    /// <see cref="ConfigureTabs"/>'s remarks).
    /// </remarks>
    private static void ConfigureSubTabs(EntityTypeBuilder<SubTab> entity)
    {
        entity.ToTable("SubTabs", ConfigureSubTabCheckConstraints);

        entity.Property(st => st.Name).HasMaxLength(200);

        entity.HasOne(st => st.Tab)
            .WithMany()
            .HasForeignKey(st => st.TabId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(st => st.EventId);
        entity.HasIndex(st => st.TabId);
    }

    private static void ConfigureSubTabCheckConstraints(TableBuilder<SubTab> table) =>
        table.HasCheckConstraint("CK_SubTabs_Name_NotEmpty", "TRIM(Name) <> ''");

    /// <remarks>
    /// <see cref="Section.EventId"/> gets no FK, same reasoning as <see cref="ConfigureTabs"/>. Both parent
    /// FKs - <see cref="Section.ParentTabId"/> and <see cref="Section.ParentSubTabId"/> (ADR-0046's P5-11
    /// amendment) - restrict, same reasoning as <see cref="ConfigureSubTabs"/>'s <see cref="SubTab"/>→<see cref="Tab"/>
    /// FK. <c>CK_Sections_ExactlyOneParent</c> is this column pair's own backstop - app code in
    /// <see cref="ActivityPlacementResourceDefinition"/> always sets exactly one when resolving-or-creating
    /// a <see cref="Section"/>.
    /// </remarks>
    private static void ConfigureSections(EntityTypeBuilder<Section> entity)
    {
        entity.ToTable("Sections", ConfigureSectionCheckConstraints);

        entity.Property(s => s.Name).HasMaxLength(200);

        entity.HasOne(s => s.ParentTab)
            .WithMany()
            .HasForeignKey(s => s.ParentTabId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(s => s.ParentSubTab)
            .WithMany()
            .HasForeignKey(s => s.ParentSubTabId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(s => s.EventId);
        entity.HasIndex(s => s.ParentTabId);
        entity.HasIndex(s => s.ParentSubTabId);
    }

    /// <remarks>
    /// Both constraints in one call, matching <see cref="ConfigureEventCheckConstraints"/>'s "repeated
    /// <c>ToTable</c> clobbers earlier configuration" rule. <c>CK_Sections_ExactlyOneParent</c> mirrors
    /// <see cref="BuildDatesOrderedCheckSql"/>'s exactly-one-of-two-nullables shape (ADR-0046's P5-11
    /// amendment), just without that constraint's ordering clause - either parent being set is
    /// interchangeable here, unlike <see cref="Event.StartsAt"/>/<see cref="Event.EndsAt"/>.
    /// </remarks>
    private static void ConfigureSectionCheckConstraints(TableBuilder<Section> table)
    {
        table.HasCheckConstraint("CK_Sections_Name_NotEmpty", "TRIM(Name) <> ''");
        table.HasCheckConstraint("CK_Sections_ExactlyOneParent", BuildExactlyOneParentCheckSql());
    }

    private static string BuildExactlyOneParentCheckSql() =>
        "(ParentTabId IS NOT NULL AND ParentSubTabId IS NULL) OR (ParentTabId IS NULL AND ParentSubTabId IS NOT NULL)";

    /// <remarks>
    /// <see cref="SubSection.EventId"/> gets no FK, same reasoning as <see cref="ConfigureTabs"/>.
    /// <see cref="SubSection"/>→<see cref="Section"/> restricts, same reasoning as
    /// <see cref="ConfigureSubTabs"/>'s <see cref="SubTab"/>→<see cref="Tab"/> FK.
    /// </remarks>
    private static void ConfigureSubSections(EntityTypeBuilder<SubSection> entity)
    {
        entity.ToTable("SubSections", ConfigureSubSectionCheckConstraints);

        entity.Property(ss => ss.Name).HasMaxLength(200);

        entity.HasOne(ss => ss.Section)
            .WithMany()
            .HasForeignKey(ss => ss.SectionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(ss => ss.EventId);
        entity.HasIndex(ss => ss.SectionId);
    }

    private static void ConfigureSubSectionCheckConstraints(TableBuilder<SubSection> table) =>
        table.HasCheckConstraint("CK_SubSections_Name_NotEmpty", "TRIM(Name) <> ''");

    /// <remarks>
    /// <see cref="ActivityPlacement.EventId"/> gets no FK at all - ADR-0050 settles it as a denormalized
    /// column app code keeps consistent via the same write-time pre-check that enforces every other
    /// Placement rule, not a DB-enforced relationship. This is deliberate, not an oversight: giving it a
    /// cascading FK to <see cref="Event"/> alongside <see cref="ActivityPlacement"/>→<see cref="Activity"/>'s
    /// own cascade (below) would be a second cascade path from <see cref="Event"/> to <c>ActivityPlacements</c>
    /// (the first being <see cref="Event"/>→<see cref="Activity"/>→<c>ActivityPlacements</c>), which SQL
    /// Server refuses at migration time. <see cref="ActivityPlacement"/>→<see cref="Activity"/> cascades - a
    /// Placement is meaningless once its Activity is gone (P5-9, #95). Every Tier FK
    /// (<see cref="ActivityPlacement.TabId"/>/<see cref="ActivityPlacement.SubTabId"/>/
    /// <see cref="ActivityPlacement.SectionId"/>/<see cref="ActivityPlacement.SubSectionId"/>) restricts -
    /// a Tier is reaped only after its last Placement reference is gone (ADR-0046), so a Placement should
    /// never be the row that vanishes because its Tier did. <see cref="ActivityPlacement.TabId"/> is
    /// <c>IsRequired()</c> despite its nullable CLR type (<c>ActivityPlacement.cs</c>'s remarks) -
    /// <see cref="ActivityPlacementResourceDefinition.OnWritingAsync"/> always resolves it before
    /// <see cref="JsonApiDotNetCore.Resources.JsonApiResourceDefinition{TResource,TId}.OnWritingAsync"/>'s
    /// own base call persists the row, so the column is never actually null. <see cref="ActivityPlacement.TabName"/>/
    /// <see cref="ActivityPlacement.SubTabName"/>/<see cref="ActivityPlacement.SectionName"/>/
    /// <see cref="ActivityPlacement.SubSectionName"/> are <c>Ignore</c>-d below - wire-only write input,
    /// never a column.
    /// </remarks>
    private static void ConfigureActivityPlacements(EntityTypeBuilder<ActivityPlacement> entity)
    {
        entity.ToTable("ActivityPlacements", ConfigureActivityPlacementCheckConstraints);

        entity.Ignore(p => p.TabName);
        entity.Ignore(p => p.SubTabName);
        entity.Ignore(p => p.SectionName);
        entity.Ignore(p => p.SubSectionName);

        entity.Property(p => p.TabId).IsRequired();

        entity.HasOne(p => p.Activity)
            .WithMany()
            .HasForeignKey(p => p.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(p => p.Tab)
            .WithMany()
            .HasForeignKey(p => p.TabId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(p => p.SubTab)
            .WithMany()
            .HasForeignKey(p => p.SubTabId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(p => p.Section)
            .WithMany()
            .HasForeignKey(p => p.SectionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(p => p.SubSection)
            .WithMany()
            .HasForeignKey(p => p.SubSectionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(p => p.EventId);
        entity.HasIndex(p => p.ActivityId);

        ConfigureActivityPlacementUniqueness(entity);
    }

    /// <remarks>
    /// The uniqueness rule is one logical constraint - "the same Activity can't occupy the identical
    /// resolved Tier path twice" (ADR-0046) - but <see cref="ActivityPlacement.SubTabId"/>/
    /// <see cref="ActivityPlacement.SectionId"/>/<see cref="ActivityPlacement.SubSectionId"/> are all
    /// independently nullable, and a plain unique index treats every NULL as distinct from every other NULL
    /// (two rows both leaving <see cref="ActivityPlacement.SectionId"/> null would never collide). Six
    /// filtered indexes, one per Sub Tab/Section/Sub Section presence combination CONTEXT.md's
    /// "independent chains" rule and <see cref="CK_ActivityPlacements_SubSectionRequiresSection"/> actually
    /// allow (Sub Tab present/absent is independent of the Section chain; Sub Section present requires
    /// Section present, cutting the Section/Sub Section pairing from four combinations to three) - the same
    /// per-nullable-shape filtered-index technique <see cref="ConfigureUserRoles"/> already uses for its one
    /// nullable column, scaled up for three.
    /// </remarks>
    private static void ConfigureActivityPlacementUniqueness(EntityTypeBuilder<ActivityPlacement> entity)
    {
        entity.HasIndex(p => new { p.ActivityId, p.TabId })
            .IsUnique()
            .HasDatabaseName("IX_ActivityPlacements_TabOnly")
            .HasFilter("[SubTabId] IS NULL AND [SectionId] IS NULL AND [SubSectionId] IS NULL");

        entity.HasIndex(p => new { p.ActivityId, p.TabId, p.SubTabId })
            .IsUnique()
            .HasDatabaseName("IX_ActivityPlacements_TabSubTab")
            .HasFilter("[SubTabId] IS NOT NULL AND [SectionId] IS NULL AND [SubSectionId] IS NULL");

        entity.HasIndex(p => new { p.ActivityId, p.TabId, p.SectionId })
            .IsUnique()
            .HasDatabaseName("IX_ActivityPlacements_TabSection")
            .HasFilter("[SubTabId] IS NULL AND [SectionId] IS NOT NULL AND [SubSectionId] IS NULL");

        entity.HasIndex(p => new { p.ActivityId, p.TabId, p.SubTabId, p.SectionId })
            .IsUnique()
            .HasDatabaseName("IX_ActivityPlacements_TabSubTabSection")
            .HasFilter("[SubTabId] IS NOT NULL AND [SectionId] IS NOT NULL AND [SubSectionId] IS NULL");

        entity.HasIndex(p => new { p.ActivityId, p.TabId, p.SectionId, p.SubSectionId })
            .IsUnique()
            .HasDatabaseName("IX_ActivityPlacements_TabSectionSubSection")
            .HasFilter("[SubTabId] IS NULL AND [SectionId] IS NOT NULL AND [SubSectionId] IS NOT NULL");

        entity.HasIndex(p => new { p.ActivityId, p.TabId, p.SubTabId, p.SectionId, p.SubSectionId })
            .IsUnique()
            .HasDatabaseName("IX_ActivityPlacements_TabSubTabSectionSubSection")
            .HasFilter("[SubTabId] IS NOT NULL AND [SectionId] IS NOT NULL AND [SubSectionId] IS NOT NULL");
    }

    /// <remarks>
    /// <c>CK_ActivityPlacements_SubSectionRequiresSection</c> backstops the "Sub Section requires a Section
    /// on the same Placement" rule <see cref="ActivityPlacementResourceDefinition.OnWritingAsync"/> enforces
    /// as a 422 - matching every other backstop-a-write-time-rule CHECK on this type, portable across SQL
    /// Server/SQLite (ADR-0014).
    /// </remarks>
    private static void ConfigureActivityPlacementCheckConstraints(TableBuilder<ActivityPlacement> table) =>
        table.HasCheckConstraint("CK_ActivityPlacements_SubSectionRequiresSection", "[SubSectionId] IS NULL OR [SectionId] IS NOT NULL");

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
