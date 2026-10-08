using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// A required, top-level grouping an Activity or InfoPage is placed under (CONTEXT.md's Tab entry) - the
/// navigation unit a visitor picks between (e.g. "Morning", "Afternoon"). Scoped to one <see cref="Event"/>.
/// </summary>
/// <remarks>
/// Exposed read-only at <c>/api/tabs</c> (P5-11, #96) - <see cref="JsonApiEndpoints.Query"/> only. A Tab is
/// never separately authored (CONTEXT.md's Tier entry): it's created inline, and reaped, exclusively inside
/// <see cref="ActivityPlacementResourceDefinition.OnWritingAsync"/> - see ADR-0072 for why this resource
/// carries no <c>Post</c>/<c>Patch</c>/<c>Delete</c> endpoints of its own. <see cref="TabResourceDefinition"/>
/// narrows a collection read to the caller's assigned Events the same way
/// <see cref="ActivityResourceDefinition.OnApplyFilter"/> does for <see cref="Activity"/>.
/// </remarks>
[Resource(GenerateControllerEndpoints = JsonApiEndpoints.Query)]
public class Tab : Identifiable<Guid>
{
    /// <summary>
    /// The <see cref="Data.Event.Id"/> this Tab belongs to. Deliberately carries no <c>Event</c> navigation
    /// property - unlike <see cref="Activity.Event"/>'s "flat attribute, not a JSON:API relationship" case,
    /// this is a plain, FK-less column at the EF level too. A Tab's lifecycle is entirely app-driven
    /// (ADR-0046: created and reaped only inside <see cref="ActivityPlacementResourceDefinition"/>), never a
    /// DB cascade side effect - see <see cref="VirtualLeadersGuideDbContext"/>'s remarks on
    /// <see cref="VirtualLeadersGuideDbContext.ConfigureTabs"/> for why a cascading FK here would conflict
    /// with SQL Server's single-cascade-path rule once <see cref="Section"/>'s two optional parents are
    /// accounted for. An Event's delete flow (<see cref="EventResourceDefinition"/>) explicitly cleans up
    /// this Event's Tabs rather than relying on cascade.
    /// </summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid EventId { get; set; }

    /// <summary>This Tab's display name (CONTEXT.md's Tab entry). Never renamed - typing a different name creates a different row (ADR-0046).</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Activity.Name"/>'s
    /// pattern - <c>CK_Tabs_Name_NotEmpty</c> (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop
    /// for anything that writes this column outside this setter.
    /// </remarks>
    [Attr]
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>This Tab's position among its Event's other Tabs - reordered inline in the Activity edit page's live tree (ADR-0049, P5-22).</summary>
    [Attr]
    public int SortOrder { get; set; }

    /// <summary>Creates a new <see cref="Tab"/> for <paramref name="eventId"/> with the given <paramref name="name"/>.</summary>
    /// <param name="eventId">The <see cref="Data.Event"/> this Tab belongs to.</param>
    /// <param name="name">This Tab's display name.</param>
    /// <param name="sortOrder">This Tab's initial position among its Event's other Tabs.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="Tab"/>.</returns>
    public static Tab Create(Guid eventId, string name, int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        Name = name,
        SortOrder = sortOrder
    };
}
