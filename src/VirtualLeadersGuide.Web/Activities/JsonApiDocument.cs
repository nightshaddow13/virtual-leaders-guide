using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Activities;

/// <summary>
/// Resource-specific JSON:API envelope shapes <see cref="ApiActivityClient"/> sends to and reads from
/// <c>/api/activities</c> - <see langword="internal"/> wire-format detail, not exposed past this client;
/// callers see <see cref="ActivityDto"/> and <see cref="ActivityWriteOutcome"/> instead. The
/// resource-shaped-nothing envelope plumbing lives in <see cref="VirtualLeadersGuide.Web.JsonApi"/> instead,
/// shared with <c>InfoPages</c>/<c>Events</c>.
/// </summary>
internal sealed class ActivityResourceObject
{
    /// <summary>The JSON:API resource type - always <c>"activities"</c>.</summary>
    public required string Type { get; init; }

    /// <summary>This Activity's id - absent on a create request body, since the id is server-generated.</summary>
    public string? Id { get; init; }

    /// <summary>This Activity's attributes.</summary>
    public ActivityAttributesDto? Attributes { get; init; }
}

/// <summary>An Activity's <c>eventId</c>/<c>name</c>/<c>description</c> attributes, as sent or received in a request/response.</summary>
/// <remarks>
/// Every property is nullable so a request can omit an attribute rather than send it as JSON <c>null</c> -
/// matching <c>InfoPages.InfoPageAttributesDto</c>'s discipline. <see cref="EventId"/> only ever appears on a
/// create request - <c>Activity.EventId</c> carries no <c>AllowChange</c>, so this story never sends it on an
/// update (there is no update yet - see <see cref="ApiActivityClient"/>'s remarks).
/// </remarks>
internal sealed class ActivityAttributesDto
{
    /// <summary>The Event this Activity belongs to - only ever sent on a create request.</summary>
    public Guid? EventId { get; init; }

    /// <summary>This Activity's display name.</summary>
    public string? Name { get; init; }

    /// <summary>This Activity's rich text description, as raw markdown.</summary>
    public string? Description { get; init; }
}

/// <summary>A single-resource JSON:API document - the request body for POST and the response body for POST.</summary>
internal sealed class ActivityDocument
{
    /// <summary>The Activity resource object this document carries.</summary>
    public required ActivityResourceObject Data { get; init; }
}
