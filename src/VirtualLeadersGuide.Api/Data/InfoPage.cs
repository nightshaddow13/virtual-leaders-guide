namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// A <see cref="Page"/> subtype holding free-form markdown content (CONTEXT.md's InfoPage entry) - About,
/// Packing List, FAQ, etc. The only <see cref="Page"/> subtype today.
/// </summary>
/// <remarks>
/// Deliberately not <c>Identifiable&lt;Guid&gt;</c> yet - see <see cref="Page"/>'s remarks. P5-16 (#21) is what
/// turns this into a JsonApiDotNetCore resource.
/// </remarks>
public class InfoPage : Page
{
    /// <summary>
    /// This InfoPage's content, as raw markdown - not sanitized HTML. Sanitizing happens at render time
    /// (ADR-0048), shared with <c>Activity.Description</c>'s mechanism, so editing an InfoPage always starts
    /// from what was actually typed.
    /// </summary>
    public required string MarkdownContent { get; set; }

    /// <summary>
    /// Creates a new <see cref="InfoPage"/> for <paramref name="eventId"/> with the given
    /// <paramref name="title"/> and <paramref name="markdownContent"/>, tagged with
    /// <see cref="PageTypeIds.InfoPage"/>.
    /// </summary>
    /// <remarks>
    /// The single construction path that keeps <see cref="Page.PageTypeId"/> truthful - see ADR-0055's
    /// Consequences for why nothing at the database level can enforce that on its own.
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
