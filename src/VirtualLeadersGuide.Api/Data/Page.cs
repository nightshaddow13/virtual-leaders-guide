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
/// Deliberately <em>not</em> <c>Identifiable&lt;Guid&gt;</c> - inheriting it would auto-register this type (and
/// every subtype) in the JsonApiDotNetCore resource graph the moment it's added to the EF model, regardless of
/// <c>[Resource]</c> (<c>AddJsonApi&lt;TDbContext&gt;</c> walks <c>DbContext.Model.GetEntityTypes()</c> and adds
/// every <c>IIdentifiable</c> type it finds), which would expose an unauthenticated-scoping <c>/api/pages</c>
/// before P5-16 (#21) builds any authorization. Same posture as <see cref="Role"/> (ADR-0017's Consequences).
/// P5-16 is the ticket that changes this base to <c>Identifiable&lt;Guid&gt;</c>, deliberately, once it's also
/// adding the <c>[Resource]</c>/authorization that makes exposure safe.
/// </remarks>
public abstract class Page
{
    public Guid Id { get; set; }

    /// <summary>The Event this Page belongs to.</summary>
    public required Guid EventId { get; set; }

    public Event? Event { get; set; }

    /// <summary>This Page's display title.</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Event.Name"/>'s pattern
    /// - <c>CK_Pages_Title_NotEmpty</c> (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for
    /// anything that writes this column outside this setter.
    /// </remarks>
    public required string Title { get; set => field = value.Trim(); }

    /// <summary>The <see cref="PageType.Id"/> naming this Page's subtype (see <see cref="PageTypeIds"/>).</summary>
    public int PageTypeId { get; set; }

    public PageType? PageType { get; set; }
}
