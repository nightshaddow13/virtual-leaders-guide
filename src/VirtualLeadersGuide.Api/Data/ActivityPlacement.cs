using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// An <see cref="Activity"/>'s appearance under a specific <see cref="Tab"/> and, optionally, a
/// <see cref="SubTab"/>/<see cref="Section"/>/<see cref="SubSection"/> (CONTEXT.md's Placement entry, ADR-0050).
/// Carries its own <see cref="SortOrder"/>, independent of any other Placement of the same Activity.
/// </summary>
/// <remarks>
/// Exposed at <c>/api/placements</c> (P5-11, #96; ADR-0050), scoped Admin/assigned-Director the same as
/// <c>/api/activities</c> (ADR-0069) - see <see cref="ActivityPlacementResourceDefinition"/> for the
/// enforcement. Schema-distinct from InfoPage's own future Placement table (ADR-0047) - this type exists
/// only for <see cref="Activity"/>.
/// </remarks>
/// <remarks>
/// <para>
/// <see cref="TabId"/>/<see cref="SubTabId"/>/<see cref="SectionId"/>/<see cref="SubSectionId"/> are the
/// persisted, resolved Tier path - flat <see cref="Attr"/> scalar FKs, never <c>[HasOne]</c>, mirroring
/// <see cref="Activity.Event"/>'s own reasoning. <see cref="TabId"/> is C#-nullable even though the column
/// is <c>NOT NULL</c> (<see cref="VirtualLeadersGuideDbContext"/> forces this via <c>IsRequired()</c>
/// despite the nullable CLR type) - a create request may omit it in favor of <see cref="TabName"/>, and
/// <see cref="ActivityPlacementResourceDefinition.OnWritingAsync"/> always resolves one of the two into a
/// concrete value before the base call persists the row, so the column itself is never actually null.
/// </para>
/// <para>
/// <see cref="TabName"/>/<see cref="SubTabName"/>/<see cref="SectionName"/>/<see cref="SubSectionName"/> are
/// a second, parallel set of attributes carrying a not-yet-existing Tier's raw name instead of an id - not
/// mapped to any column (<see cref="VirtualLeadersGuideDbContext"/> ignores them), purely a write-time input
/// channel <see cref="ActivityPlacementResourceDefinition.OnWritingAsync"/> consumes to resolve-or-create the
/// corresponding Tier row (ADR-0072). A caller supplies at most one of each id/name pair per level - never
/// both, never neither, for whichever levels this Placement sets.
/// </para>
/// </remarks>
[Resource(PublicName = "placements",
    GenerateControllerEndpoints = JsonApiEndpoints.Query | JsonApiEndpoints.Post | JsonApiEndpoints.Patch | JsonApiEndpoints.Delete)]
public class ActivityPlacement : Identifiable<Guid>
{
    /// <summary>
    /// The <see cref="Data.Event.Id"/> this Placement belongs to - denormalized from <see cref="Activity"/>'s
    /// own Event (ADR-0050). Deliberately carries no <c>Event</c> navigation property at all, unlike every
    /// other Event-scoped entity in this schema - a navigation property paired with a same-named
    /// <c>EventId</c> is exactly what EF Core's conventions need to wire up a real cascading foreign key, and
    /// a second cascade path from <see cref="Data.Event"/> to <c>ActivityPlacements</c> (alongside
    /// <see cref="Activity"/>'s own) is what SQL Server refuses at migration time - see
    /// <see cref="VirtualLeadersGuideDbContext"/>'s remarks on this type for the full reasoning. Kept
    /// consistent by the same write-time pre-check that enforces every other Placement rule, not a DB-level
    /// relationship.
    /// </summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid EventId { get; set; }

    /// <summary>The <see cref="Activity"/> this Placement is for. Set at creation and permanent - no <see cref="AttrCapabilities.AllowChange"/>, matching <see cref="Activity.EventId"/>'s own immutable-FK idiom.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid ActivityId { get; set; }

    /// <summary>The Activity this Placement is for. Not <c>[HasOne]</c>, mirroring <see cref="Activity.Event"/>.</summary>
    public Activity? Activity { get; set; }

    /// <summary>
    /// The resolved <see cref="Tab"/> this Placement is under - required on every Placement, but
    /// C#-nullable; see this type's remarks for why. Exactly one of this or <see cref="TabName"/> is
    /// supplied on a create request.
    /// </summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate | AttrCapabilities.AllowFilter)]
    public Guid? TabId { get; set; }

    /// <summary>The resolved Tab. Not <c>[HasOne]</c>, mirroring <see cref="Activity.Event"/>.</summary>
    public Tab? Tab { get; set; }

    /// <summary>A not-yet-existing Tab's raw name, supplied instead of <see cref="TabId"/> - never persisted; see this type's remarks.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowCreate)]
    public string? TabName { get; set; }

    /// <summary>The resolved <see cref="SubTab"/> this Placement is under, if any. Exactly one of this or <see cref="SubTabName"/> is supplied when this Placement sets a Sub Tab at all.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate | AttrCapabilities.AllowFilter)]
    public Guid? SubTabId { get; set; }

    /// <summary>The resolved Sub Tab. Not <c>[HasOne]</c>, mirroring <see cref="Activity.Event"/>.</summary>
    public SubTab? SubTab { get; set; }

    /// <summary>A not-yet-existing Sub Tab's raw name, supplied instead of <see cref="SubTabId"/> - never persisted; see this type's remarks.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowCreate)]
    public string? SubTabName { get; set; }

    /// <summary>The resolved <see cref="Section"/> this Placement is under, if any. Exactly one of this or <see cref="SectionName"/> is supplied when this Placement sets a Section at all.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate | AttrCapabilities.AllowFilter)]
    public Guid? SectionId { get; set; }

    /// <summary>The resolved Section. Not <c>[HasOne]</c>, mirroring <see cref="Activity.Event"/>.</summary>
    public Section? Section { get; set; }

    /// <summary>A not-yet-existing Section's raw name, supplied instead of <see cref="SectionId"/> - never persisted; see this type's remarks. Its parent (<see cref="Data.Section.ParentTabId"/> or <see cref="Data.Section.ParentSubTabId"/>) is derived from whether this same Placement also resolves a Sub Tab, not supplied separately (ADR-0046).</summary>
    [Attr(Capabilities = AttrCapabilities.AllowCreate)]
    public string? SectionName { get; set; }

    /// <summary>The resolved <see cref="SubSection"/> this Placement is under, if any. Exactly one of this or <see cref="SubSectionName"/> is supplied when this Placement sets a Sub Section at all - and either requires a Section resolved on this same Placement (<see cref="SectionId"/> or <see cref="SectionName"/>).</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate | AttrCapabilities.AllowFilter)]
    public Guid? SubSectionId { get; set; }

    /// <summary>The resolved Sub Section. Not <c>[HasOne]</c>, mirroring <see cref="Activity.Event"/>.</summary>
    public SubSection? SubSection { get; set; }

    /// <summary>A not-yet-existing Sub Section's raw name, supplied instead of <see cref="SubSectionId"/> - never persisted; see this type's remarks.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowCreate)]
    public string? SubSectionName { get; set; }

    /// <summary>
    /// This Placement's position among every other Placement sharing its exact Tier path - independent of
    /// any other Placement of the same Activity (ADR-0050). Reordered by P5-13 (#98); this story only ever
    /// assigns an initial value.
    /// </summary>
    [Attr]
    public int SortOrder { get; set; }

    /// <summary>
    /// Creates a new <see cref="ActivityPlacement"/> from an already-resolved Tier path - used by
    /// <see cref="ActivityPlacementResourceDefinition.OnWritingAsync"/> once every id/name pair above has
    /// been resolved to a concrete id, and by test seeding. <see cref="TabName"/>/<see cref="SubTabName"/>/
    /// <see cref="SectionName"/>/<see cref="SubSectionName"/> play no part here - they exist only to carry
    /// wire input, never a resolved value.
    /// </summary>
    /// <param name="eventId">The <see cref="Data.Event"/> this Placement belongs to.</param>
    /// <param name="activityId">The <see cref="Activity"/> this Placement is for.</param>
    /// <param name="tabId">The resolved Tab.</param>
    /// <param name="subTabId">The resolved Sub Tab, if this Placement sets one.</param>
    /// <param name="sectionId">The resolved Section, if this Placement sets one.</param>
    /// <param name="subSectionId">The resolved Sub Section, if this Placement sets one.</param>
    /// <param name="sortOrder">This Placement's initial position among others sharing its exact Tier path.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="ActivityPlacement"/>.</returns>
    public static ActivityPlacement Create(
        Guid eventId, Guid activityId, Guid tabId, Guid? subTabId, Guid? sectionId, Guid? subSectionId, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        ActivityId = activityId,
        TabId = tabId,
        SubTabId = subTabId,
        SectionId = sectionId,
        SubSectionId = subSectionId,
        SortOrder = sortOrder
    };
}
