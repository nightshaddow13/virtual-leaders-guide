using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// A <see cref="Page"/> subtype holding free-form markdown content (CONTEXT.md's InfoPage entry) - About,
/// Packing List, FAQ, etc. The only <see cref="Page"/> subtype today.
/// </summary>
/// <remarks>
/// Exposed at <c>/api/infoPages</c> (P5-16, #21), full CRUD, scoped Admin/assigned-Director the same as
/// <c>/api/events</c> - see <see cref="InfoPageResourceDefinition"/> for the enforcement and ADR-0059 for why
/// a Director's write authority here is broader than <see cref="Authorization.EventAccessPolicy.CanUpdate"/>'s
/// Admin-only rule. May exist with no Placement at all - Placement (#86/#91/#92) is a separate resource, and
/// "written but not yet placed" is a normal state, not a draft one.
/// </remarks>
[Resource]
public class InfoPage : Page
{
    /// <summary>
    /// This InfoPage's content, as raw markdown - not sanitized HTML. Sanitizing happens at render time
    /// (ADR-0048), shared with <c>Activity.Description</c>'s mechanism, so editing an InfoPage always starts
    /// from what was actually typed. An empty string is legal content - "add the page, then write it" is a
    /// normal authoring order, and required on the wire regardless (so a caller states that emptiness
    /// explicitly rather than omitting the field).
    /// </summary>
    [Attr]
    public required string MarkdownContent { get; set; }

    /// <summary>
    /// Creates a new <see cref="InfoPage"/> for <paramref name="eventId"/> with the given
    /// <paramref name="title"/> and <paramref name="markdownContent"/>, tagged with
    /// <see cref="PageTypeIds.InfoPage"/>.
    /// </summary>
    /// <remarks>
    /// The single construction path that keeps <see cref="Page.PageTypeId"/> truthful for anything not going
    /// through HTTP - see ADR-0055's Consequences for why nothing at the database level can enforce that on
    /// its own. JsonApiDotNetCore constructs a resource through its own resource factory and never routes a
    /// POST body through this method, so <see cref="InfoPageResourceDefinition.FillServerGeneratedDefaults"/>
    /// mirrors this method's <see cref="Page.PageTypeId"/> assignment for the HTTP path.
    /// </remarks>
    /// <param name="eventId">The <see cref="Event"/> this InfoPage belongs to.</param>
    /// <param name="title">This InfoPage's display title.</param>
    /// <param name="markdownContent">This InfoPage's content, as raw markdown.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="InfoPage"/>.</returns>
    public static InfoPage Create(Guid eventId, string title, string markdownContent) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        Title = title,
        PageTypeId = PageTypeIds.InfoPage,
        MarkdownContent = markdownContent
    };
}
