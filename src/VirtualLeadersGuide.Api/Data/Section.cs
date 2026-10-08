using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// An optional page-structure grouping for an Activity Placement (CONTEXT.md's Section entry) - a heading
/// wherever it's placed, not a navigation unit. Scoped to whichever immediate parent the Placement gives it:
/// the bare <see cref="Tab"/>, when that Placement has no <see cref="SubTab"/>, or that specific
/// <see cref="SubTab"/>, when it does. Never set on an InfoPage's Placement (ADR-0047).
/// </summary>
/// <remarks>
/// Exposed read-only at <c>/api/sections</c> (P5-11, #96) - see <see cref="Tab"/>'s remarks and ADR-0072 for
/// why this resource carries no <c>Post</c>/<c>Patch</c>/<c>Delete</c> endpoints of its own. Its dual-nullable
/// parent FK shape (<see cref="ParentTabId"/>/<see cref="ParentSubTabId"/>) is ADR-0046's P5-11 amendment.
/// </remarks>
[Resource(GenerateControllerEndpoints = JsonApiEndpoints.Query)]
public class Section : Identifiable<Guid>
{
    /// <summary>The <see cref="Data.Event.Id"/> this Section belongs to - denormalized, deliberately carrying no navigation property or FK, same reasoning as <see cref="Tab.EventId"/>.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid EventId { get; set; }

    /// <summary>
    /// The <see cref="Tab"/> this Section is scoped to, when the Placement that created it set no
    /// <see cref="SubTab"/> - mutually exclusive with <see cref="ParentSubTabId"/>; exactly one is set
    /// (<c>CK_Sections_ExactlyOneParent</c>, <see cref="VirtualLeadersGuideDbContext"/>).
    /// </summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter)]
    public Guid? ParentTabId { get; set; }

    /// <summary>The parent Tab, when <see cref="ParentTabId"/> is set. Not <c>[HasOne]</c>, mirroring <see cref="Tab.Event"/>.</summary>
    public Tab? ParentTab { get; set; }

    /// <summary>
    /// The <see cref="SubTab"/> this Section is scoped to, when the Placement that created it set one -
    /// mutually exclusive with <see cref="ParentTabId"/>; exactly one is set
    /// (<c>CK_Sections_ExactlyOneParent</c>, <see cref="VirtualLeadersGuideDbContext"/>).
    /// </summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter)]
    public Guid? ParentSubTabId { get; set; }

    /// <summary>The parent Sub Tab, when <see cref="ParentSubTabId"/> is set. Not <c>[HasOne]</c>, mirroring <see cref="Tab.Event"/>.</summary>
    public SubTab? ParentSubTab { get; set; }

    /// <summary>This Section's display name (CONTEXT.md's Section entry). Never renamed (ADR-0046).</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Tab.Name"/>'s pattern
    /// - <c>CK_Sections_Name_NotEmpty</c> (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for
    /// anything that writes this column outside this setter.
    /// </remarks>
    [Attr]
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>This Section's position among its parent's other Sections - reordered inline in the Activity edit page's live tree (ADR-0049, P5-22).</summary>
    [Attr]
    public int SortOrder { get; set; }

    /// <summary>Creates a new <see cref="Section"/> scoped to a bare <see cref="Tab"/> (no Sub Tab in the triggering Placement).</summary>
    /// <param name="eventId">The <see cref="Data.Event"/> this Section belongs to.</param>
    /// <param name="parentTabId">The <see cref="Tab"/> this Section is scoped to.</param>
    /// <param name="name">This Section's display name.</param>
    /// <param name="sortOrder">This Section's initial position among its parent's other Sections.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="Section"/>.</returns>
    public static Section CreateUnderTab(Guid eventId, Guid parentTabId, string name, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        ParentTabId = parentTabId,
        Name = name,
        SortOrder = sortOrder
    };

    /// <summary>Creates a new <see cref="Section"/> scoped to a <see cref="SubTab"/>.</summary>
    /// <param name="eventId">The <see cref="Data.Event"/> this Section belongs to.</param>
    /// <param name="parentSubTabId">The <see cref="SubTab"/> this Section is scoped to.</param>
    /// <param name="name">This Section's display name.</param>
    /// <param name="sortOrder">This Section's initial position among its parent's other Sections.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="Section"/>.</returns>
    public static Section CreateUnderSubTab(Guid eventId, Guid parentSubTabId, string name, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        ParentSubTabId = parentSubTabId,
        Name = name,
        SortOrder = sortOrder
    };
}
