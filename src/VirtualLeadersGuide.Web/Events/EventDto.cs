namespace VirtualLeadersGuide.Web.Events;

/// <summary>The dashboard's view of a single Event, mapped from Api's <c>/api/events</c> resource (P2-7, #16).</summary>
public sealed class EventDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required string Passcode { get; init; }

    /// <summary>This Event's position in its lifecycle (CONTEXT.md's Status entry).</summary>
    /// <remarks>
    /// <see cref="EventStatus.Past"/> is computed by Api at read time, never stored - what this DTO carries
    /// is always the *effective* value already (Api's <c>OnSerialize</c>), never the raw stored column.
    /// </remarks>
    public required EventStatus Status { get; init; }

    /// <summary>When this Event starts, in UTC as Api stores it (CONTEXT.md's Starts at / Ends at entry).</summary>
    /// <remarks>
    /// <see langword="null"/> when unset - a real, valid state, not an incomplete one. Rendered to a viewer
    /// via <see cref="EventDateRange"/>, which converts from UTC into that viewer's own browser timezone
    /// (<see cref="VirtualLeadersGuide.Web.Time.BrowserTimeZoneAccessor"/>) - never displayed as the raw UTC
    /// instant.
    /// </remarks>
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>When this Event ends - see <see cref="StartsAt"/>'s remarks for the shared rules governing both.</summary>
    public DateTimeOffset? EndsAt { get; init; }
}
