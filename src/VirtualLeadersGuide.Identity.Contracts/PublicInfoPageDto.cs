namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// The public, anonymous-safe view of an InfoPage, returned by
/// <c>PublicGuideRoutes.InfoPagesByEvent</c> (P4-1, #23).
/// </summary>
/// <remarks>
/// No <c>EventId</c> - the request is already scoped by the Event's Slug. <see cref="MarkdownContent"/> is raw
/// markdown, never sanitized HTML, matching <c>InfoPage.MarkdownContent</c>'s own contract - Web renders it
/// through <c>Markdown.MarkdownRenderer</c> the same way the dashboard's authoring preview does (ADR-0048).
/// </remarks>
public sealed class PublicInfoPageDto
{
    public required Guid Id { get; set; }

    public required string Title { get; set; }

    public required string MarkdownContent { get; set; }
}
