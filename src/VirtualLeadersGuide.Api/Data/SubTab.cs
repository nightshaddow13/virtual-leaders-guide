using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// An optional second-level navigation grouping, scoped to one <see cref="Tab"/> (CONTEXT.md's Sub Tab
/// entry, e.g. "Round Robin" under "Morning"). Same lazy-create, auto-delete lifecycle as <see cref="Tab"/>.
/// </summary>
/// <remarks>
/// Exposed read-only at <c>/api/subTabs</c> (P5-11, #96) - see <see cref="Tab"/>'s remarks and ADR-0072 for
/// why this resource carries no <c>Post</c>/<c>Patch</c>/<c>Delete</c> endpoints of its own.
/// </remarks>
[Resource(GenerateControllerEndpoints = JsonApiEndpoints.Query)]
public class SubTab : Identifiable<Guid>
{
    /// <summary>The <see cref="Data.Event.Id"/> this Sub Tab belongs to - denormalized from <see cref="Tab"/>'s own Event, deliberately carrying no navigation property or FK, same reasoning as <see cref="Tab.EventId"/>.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid EventId { get; set; }

    /// <summary>The <see cref="Tab"/> this Sub Tab is scoped to.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter)]
    public required Guid TabId { get; set; }

    /// <summary>The Tab this Sub Tab belongs to. Not <c>[HasOne]</c>, mirroring <see cref="Tab.Event"/>.</summary>
    public Tab? Tab { get; set; }

    /// <summary>This Sub Tab's display name (CONTEXT.md's Sub Tab entry). Never renamed (ADR-0046).</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Tab.Name"/>'s pattern
    /// - <c>CK_SubTabs_Name_NotEmpty</c> (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for
    /// anything that writes this column outside this setter.
    /// </remarks>
    [Attr]
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>This Sub Tab's position among its Tab's other Sub Tabs - reordered inline in the Activity edit page's live tree (ADR-0049, P5-22).</summary>
    [Attr]
    public int SortOrder { get; set; }

    /// <summary>Creates a new <see cref="SubTab"/> under <paramref name="tabId"/> with the given <paramref name="name"/>.</summary>
    /// <param name="eventId">The <see cref="Data.Event"/> this Sub Tab belongs to.</param>
    /// <param name="tabId">The <see cref="Tab"/> this Sub Tab is scoped to.</param>
    /// <param name="name">This Sub Tab's display name.</param>
    /// <param name="sortOrder">This Sub Tab's initial position among its Tab's other Sub Tabs.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="SubTab"/>.</returns>
    public static SubTab Create(Guid eventId, Guid tabId, string name, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        TabId = tabId,
        Name = name,
        SortOrder = sortOrder
    };
}
