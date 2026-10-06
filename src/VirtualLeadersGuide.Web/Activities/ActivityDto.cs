namespace VirtualLeadersGuide.Web.Activities;

/// <summary>The dashboard's view of a single Activity, mapped from Api's <c>/api/activities</c> resource (P5-6, #87).</summary>
public sealed class ActivityDto
{
    /// <summary>This Activity's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The Event this Activity belongs to - immutable once created (<c>Activity.EventId</c> carries no <c>AllowChange</c>).</summary>
    public required Guid EventId { get; init; }

    /// <summary>This Activity's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Raw markdown, as authored - never sanitized HTML. See <see cref="VirtualLeadersGuide.Web.Markdown.MarkdownRenderer"/>.</summary>
    public required string Description { get; init; }
}
