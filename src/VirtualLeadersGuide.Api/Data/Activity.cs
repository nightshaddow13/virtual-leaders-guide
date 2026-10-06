using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// A single thing happening at an Event (CONTEXT.md's Activity entry) - has a <see cref="Name"/> and a rich
/// text <see cref="Description"/>, and belongs to exactly one <see cref="Event"/>. Real and listable before
/// it has any Placement (P5-11, #96) - Placement is a separate, later concern.
/// </summary>
/// <remarks>
/// Exposed at <c>/api/activities</c> (P5-6, #87), scoped Admin/assigned-Director the same as
/// <c>/api/infoPages</c> - see <see cref="ActivityResourceDefinition"/> for the enforcement and ADR-0069 for
/// why a Director's write authority here is broader than <see cref="Authorization.EventAccessPolicy.CanUpdate"/>'s
/// Admin-only rule, the same call ADR-0059 already made for <see cref="InfoPage"/>.
/// </remarks>
[Resource]
public class Activity : Identifiable<Guid>
{
    /// <summary>The <see cref="Data.Event.Id"/> this Activity belongs to. Set at creation and permanent - no <see cref="AttrCapabilities.AllowChange"/>.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid EventId { get; set; }

    /// <summary>
    /// The Event this Activity belongs to. Not <c>[HasOne]</c> - <see cref="EventId"/> is a flat attribute,
    /// not a JSON:API relationship (mirrors <see cref="Page.Event"/>), so a Director can never traverse from
    /// an in-scope Activity to an out-of-scope Event.
    /// </summary>
    public Event? Event { get; set; }

    /// <summary>This Activity's display name (CONTEXT.md's Activity entry).</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Data.Event.Name"/>'s
    /// and <see cref="Page.Title"/>'s pattern - <c>CK_Activities_Name_NotEmpty</c>
    /// (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for anything that writes this column
    /// outside this setter.
    /// </remarks>
    [Attr]
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>
    /// This Activity's rich text description, as raw markdown - not sanitized HTML. Sanitizing happens at
    /// render time (ADR-0048), sharing <see cref="InfoPage.MarkdownContent"/>'s mechanism exactly. An empty
    /// string is legal content - required on the wire regardless (so a caller states that emptiness
    /// explicitly rather than omitting the field).
    /// </summary>
    [Attr]
    public required string Description { get; set; }

    /// <summary>Creates a new <see cref="Activity"/> for <paramref name="eventId"/> with the given <paramref name="name"/> and <paramref name="description"/>.</summary>
    /// <param name="eventId">The <see cref="Data.Event"/> this Activity belongs to.</param>
    /// <param name="name">This Activity's display name.</param>
    /// <param name="description">This Activity's rich text description, as raw markdown.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="Activity"/>.</returns>
    public static Activity Create(Guid eventId, string name, string description) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        Name = name,
        Description = description
    };
}
