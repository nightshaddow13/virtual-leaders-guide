using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// An optional sub-heading nested one level under a <see cref="Section"/> (CONTEXT.md's Sub Section entry) -
/// the deepest Tier; nothing nests under it. Scoped to its Section, so it inherits Section's InfoPage
/// restriction: never set on an InfoPage's Placement (ADR-0047).
/// </summary>
/// <remarks>
/// Exposed read-only at <c>/api/subSections</c> (P5-11, #96) - see <see cref="Tab"/>'s remarks and ADR-0072
/// for why this resource carries no <c>Post</c>/<c>Patch</c>/<c>Delete</c> endpoints of its own.
/// </remarks>
[Resource(GenerateControllerEndpoints = JsonApiEndpoints.Query)]
public class SubSection : Identifiable<Guid>
{
    /// <summary>The <see cref="Data.Event.Id"/> this Sub Section belongs to - denormalized, deliberately carrying no navigation property or FK, same reasoning as <see cref="Tab.EventId"/>.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid EventId { get; set; }

    /// <summary>The <see cref="Section"/> this Sub Section is scoped to - always set, unlike <see cref="Section"/>'s own dual-nullable parent.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter)]
    public required Guid SectionId { get; set; }

    /// <summary>The Section this Sub Section belongs to. Not <c>[HasOne]</c>, mirroring <see cref="Tab.Event"/>.</summary>
    public Section? Section { get; set; }

    /// <summary>This Sub Section's display name (CONTEXT.md's Sub Section entry). Never renamed (ADR-0046).</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Tab.Name"/>'s pattern
    /// - <c>CK_SubSections_Name_NotEmpty</c> (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for
    /// anything that writes this column outside this setter.
    /// </remarks>
    [Attr]
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>This Sub Section's position among its Section's other Sub Sections - reordered inline in the Activity edit page's live tree (ADR-0049, P5-22).</summary>
    [Attr]
    public int SortOrder { get; set; }

    /// <summary>Creates a new <see cref="SubSection"/> under <paramref name="sectionId"/> with the given <paramref name="name"/>.</summary>
    /// <param name="eventId">The <see cref="Data.Event"/> this Sub Section belongs to.</param>
    /// <param name="sectionId">The <see cref="Section"/> this Sub Section is scoped to.</param>
    /// <param name="name">This Sub Section's display name.</param>
    /// <param name="sortOrder">This Sub Section's initial position among its Section's other Sub Sections.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="SubSection"/>.</returns>
    public static SubSection Create(Guid eventId, Guid sectionId, string name, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        SectionId = sectionId,
        Name = name,
        SortOrder = sortOrder
    };
}
