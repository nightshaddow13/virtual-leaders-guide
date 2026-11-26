using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// An <see cref="Data.InfoPage"/>'s appearance under a <see cref="Data.Tab"/> and, optionally, a
/// <see cref="Data.SubTab"/> (CONTEXT.md's Placement entry; ADR-0047) - the one screen that InfoPage fills.
/// </summary>
/// <remarks>
/// <para>
/// Exposed at <c>/api/infoPagePlacements</c> (P5-24, #208), scoped Admin/assigned-Director like
/// <c>/api/infoPages</c> - see <see cref="InfoPagePlacementResourceDefinition"/>. Schema-distinct from
/// <see cref="ActivityPlacement"/>: two Tier levels instead of four, no <c>SortOrder</c> (a screen holds one
/// InfoPage, so there is nothing to order), and at most one per InfoPage.
/// </para>
/// <para>
/// Unlike <see cref="ActivityPlacement"/> the path is changeable: moving an InfoPage is one atomic PATCH, so a
/// rejected move leaves it where it was (ADR-0047's amendment). <see cref="TabName"/>/<see cref="SubTabName"/>
/// are write-only inputs for a Tier that may not exist yet, as on <see cref="ActivityPlacement"/> (ADR-0072).
/// </para>
/// </remarks>
[Resource(PublicName = "infoPagePlacements",
    GenerateControllerEndpoints = JsonApiEndpoints.Query | JsonApiEndpoints.Post | JsonApiEndpoints.Patch | JsonApiEndpoints.Delete)]
public class InfoPagePlacement : Identifiable<Guid>
{
    /// <summary>
    /// The <see cref="Data.Event.Id"/> this Placement belongs to, denormalized from the InfoPage's own. No
    /// <c>Event</c> navigation, for the same reason as <see cref="ActivityPlacement.EventId"/>.
    /// </summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid EventId { get; set; }

    /// <summary>The InfoPage this Placement is for. Set at creation and permanent.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid InfoPageId { get; set; }

    /// <summary>The InfoPage this Placement is for. Not <c>[HasOne]</c>, mirroring <see cref="ActivityPlacement.Activity"/>.</summary>
    public InfoPage? InfoPage { get; set; }

    /// <summary>The resolved Tab. C#-nullable but <c>NOT NULL</c> in the store - see <see cref="ActivityPlacement"/>'s remarks.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate | AttrCapabilities.AllowChange
        | AttrCapabilities.AllowFilter)]
    public Guid? TabId { get; set; }

    /// <summary>The resolved Tab. Not <c>[HasOne]</c>.</summary>
    public Tab? Tab { get; set; }

    /// <summary>A Tab's name, supplied instead of <see cref="TabId"/> - never persisted.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowCreate | AttrCapabilities.AllowChange)]
    public string? TabName { get; set; }

    /// <summary>The resolved Sub Tab, or <see langword="null"/> when the InfoPage fills the bare Tab's screen.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate | AttrCapabilities.AllowChange
        | AttrCapabilities.AllowFilter)]
    public Guid? SubTabId { get; set; }

    /// <summary>The resolved Sub Tab. Not <c>[HasOne]</c>.</summary>
    public SubTab? SubTab { get; set; }

    /// <summary>A Sub Tab's name, supplied instead of <see cref="SubTabId"/> - never persisted.</summary>
    [Attr(Capabilities = AttrCapabilities.AllowCreate | AttrCapabilities.AllowChange)]
    public string? SubTabName { get; set; }

    /// <summary>Creates a new <see cref="InfoPagePlacement"/> from an already-resolved path - used by test seeding.</summary>
    /// <param name="eventId">The Event this Placement belongs to.</param>
    /// <param name="infoPageId">The InfoPage being placed.</param>
    /// <param name="tabId">The resolved Tab.</param>
    /// <param name="subTabId">The resolved Sub Tab, if any.</param>
    /// <returns>The newly constructed, not-yet-persisted Placement.</returns>
    public static InfoPagePlacement Create(Guid eventId, Guid infoPageId, Guid tabId, Guid? subTabId) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        InfoPageId = infoPageId,
        TabId = tabId,
        SubTabId = subTabId
    };
}
