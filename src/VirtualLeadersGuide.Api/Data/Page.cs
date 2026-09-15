using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// A piece of content attached to an Event's Leaders Guide (CONTEXT.md's Page entry). Abstract - every row has
/// a matching subtype row (<see cref="InfoPage"/> today); a bare <see cref="Page"/> with no subtype content
/// isn't a real state.
/// </summary>
/// <remarks>
/// Mapped Table-Per-Type (<see cref="VirtualLeadersGuideDbContext"/>): shared columns here, subtype-specific
/// columns on each derived table, joined on this shared <see cref="Id"/> - see ADR-0055 for why TPT over TPH,
/// and for why <see cref="PageTypeId"/> exists at all despite TPT already distinguishing subtypes by table.
/// </remarks>
/// <remarks>
/// <c>Identifiable&lt;Guid&gt;</c> since P5-16 (#21), so <see cref="InfoPage"/> can be a JsonApiDotNetCore
/// resource - but this base type itself is deliberately removed from the resource graph
/// (<c>Program.cs</c>'s <c>AddJsonApi</c> <c>resources:</c> callback) and carries no <c>[Resource]</c>, so it
/// has no endpoint of its own. See ADR-0059 for why, including the corrected mechanism (a JsonApiDotNetCore
/// controller is generated from <c>[Resource]</c>, not from resource-graph membership - ADR-0055's original
/// claim to the contrary was wrong).
/// </remarks>
public abstract class Page : Identifiable<Guid>
{
    /// <summary>
    /// The <see cref="Event.Id"/> this Page belongs to. Set at creation and permanent - no
    /// <see cref="AttrCapabilities.AllowChange"/>, so re-parenting a Page to a different Event is not a
    /// supported operation (ADR-0059).
    /// </summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid EventId { get; set; }

    /// <summary>
    /// The Event this Page belongs to. Not <c>[HasOne]</c> - <see cref="EventId"/> is a flat attribute, not a
    /// JSON:API relationship (mirrors <see cref="UserRole.EventId"/>), so <c>?include=event</c> is unavailable
    /// and a Director can never traverse from an in-scope Page to an out-of-scope Event.
    /// </summary>
    public Event? Event { get; set; }

    /// <summary>This Page's display title.</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Event.Name"/>'s pattern
    /// - <c>CK_Pages_Title_NotEmpty</c> (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for
    /// anything that writes this column outside this setter. No uniqueness rule, even within one Event: a
    /// Page is reached by picking its Placement's Tab, not its Title (CONTEXT.md's InfoPage entry).
    /// </remarks>
    [Attr]
    public required string Title { get; set => field = value.Trim(); }

    /// <summary>
    /// The <see cref="PageType.Id"/> naming this Page's subtype (see <see cref="PageTypeIds"/>). Not an
    /// <c>[Attr]</c> - <see cref="PageType"/> stays unexposed (same posture as <see cref="Role"/>), and a
    /// caller never sets this directly; <see cref="InfoPageResourceDefinition"/> fills it server-side.
    /// </summary>
    public int PageTypeId { get; set; }

    /// <summary>This Page's subtype (see <see cref="PageTypeId"/>). Not <c>[HasOne]</c> - see <see cref="PageTypeId"/>.</summary>
    public PageType? PageType { get; set; }
}
