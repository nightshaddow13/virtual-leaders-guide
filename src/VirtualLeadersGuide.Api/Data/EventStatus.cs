using System.Text.Json.Serialization;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>An Event's position in its lifecycle (CONTEXT.md's Status entry; ADR-0044).</summary>
/// <remarks>
/// <see cref="Draft"/> is <c>Event.Status</c>'s default. Legal transitions are <see cref="Draft"/> →
/// <see cref="Live"/> and <see cref="Live"/> → <see cref="Cancelled"/> only, both Admin-only, both enforced
/// by <see cref="EventResourceDefinition"/> - see its remarks. <c>[JsonConverter]</c> on the type (not
/// <c>JsonApiOptions.SerializerOptions</c> globally) makes every value PascalCase on the wire and in every
/// <c>filter=</c> query - see ADR-0053 for why that's forced, not a style choice.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EventStatus>))]
public enum EventStatus
{
    /// <summary>The default for a new or duplicated Event - visible to any Director already granted access, same as any other Status.</summary>
    Draft,

    /// <summary>Published - an Admin sets this manually, independent of <see cref="Event.StartsAt"/>/<see cref="Event.EndsAt"/>.</summary>
    Live,

    /// <summary>
    /// Automatic once a <see cref="Live"/> Event's <see cref="Event.EndsAt"/> elapses. Never applies to a
    /// <see cref="Draft"/> Event, since nothing was ever public to conclude.
    /// </summary>
    /// <remarks>
    /// Never persisted - the <c>Events</c> table's <c>CK_Events_Status_Allowed</c> constraint (see
    /// <see cref="VirtualLeadersGuideDbContext"/>) forbids it as a stored value. Exists as a real member here
    /// purely so <c>filter=equals(status,'Past')</c> parses at all (JsonApiDotNetCore's filter parser needs a
    /// matching <see cref="Enum"/> member before <see cref="EventResourceDefinition.OnApplyFilter"/> ever gets
    /// a chance to rewrite it) and so <see cref="EventResourceDefinition.OnSerialize"/> has a value to assign.
    /// See ADR-0053.
    /// </remarks>
    Past,

    /// <summary>
    /// Manual, only reachable from <see cref="Live"/>, and terminal - the record that a gathering stopped
    /// happening, not a way to hide a <see cref="Draft"/> that never had an audience.
    /// </summary>
    Cancelled
}
