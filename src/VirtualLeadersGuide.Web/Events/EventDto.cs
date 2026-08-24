namespace VirtualLeadersGuide.Web.Events;

/// <summary>The dashboard's view of a single Event, mapped from Api's <c>/api/events</c> resource (P2-7, #16).</summary>
public sealed class EventDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required string Passcode { get; init; }
}
