namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>The dashboard's view of a single InfoPage, mapped from Api's <c>/api/infoPages</c> resource (P5-16, #21).</summary>
public sealed class InfoPageDto
{
    public required Guid Id { get; init; }

    /// <summary>The Event this InfoPage belongs to - immutable once created (<c>Page.EventId</c> carries no <c>AllowChange</c>).</summary>
    public required Guid EventId { get; init; }

    public required string Title { get; init; }

    /// <summary>Raw markdown, as authored - never sanitized HTML. See <see cref="VirtualLeadersGuide.Web.Markdown.MarkdownRenderer"/>.</summary>
    public required string MarkdownContent { get; init; }
}
