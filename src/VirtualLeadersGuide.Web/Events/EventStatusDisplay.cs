using Radzen;

namespace VirtualLeadersGuide.Web.Events;

/// <summary>
/// The badge text/style for each <see cref="EventStatus"/> - shared by <c>Dashboard.razor</c>'s STATUS column
/// and <c>EventEditor.razor</c>'s header/read-only badges so the two can't drift, the same way
/// <see cref="EventDeleteConfirmation"/> centralizes its own copy.
/// </summary>
internal static class EventStatusDisplay
{
    /// <summary>SHOUTY badge text, matching this app's existing badge convention (e.g. "VIEW ONLY", "ACTIVE").</summary>
    public static string Text(EventStatus status) => status switch
    {
        EventStatus.Draft => "DRAFT",
        EventStatus.Live => "LIVE",
        EventStatus.Past => "PAST",
        EventStatus.Cancelled => "CANCELLED",
        _ => status.ToString().ToUpperInvariant()
    };

    /// <summary>The <see cref="RadzenBadge"/> style for each Status - Live reads positively, Cancelled negatively, the two terminal-but-neutral states read as Light.</summary>
    public static BadgeStyle Style(EventStatus status) => status switch
    {
        EventStatus.Draft => BadgeStyle.Light,
        EventStatus.Live => BadgeStyle.Success,
        EventStatus.Past => BadgeStyle.Light,
        EventStatus.Cancelled => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };
}
