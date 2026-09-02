using System.Text.Json.Serialization;

namespace VirtualLeadersGuide.Web.Events;

/// <summary>The Web-side mirror of Api's <c>EventStatus</c> (CONTEXT.md's Status entry; ADR-0044).</summary>
/// <remarks>
/// <c>[JsonConverter]</c> on the type makes every value PascalCase on the wire, matching Api's own converter
/// - see ADR-0053 for why that's forced (JsonApiDotNetCore's filter parser is case-sensitive), not a style
/// choice.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EventStatus>))]
public enum EventStatus
{
    /// <summary>The default for a new or duplicated Event - visible to any Director already granted access, same as any other Status.</summary>
    Draft,

    /// <summary>Published - an Admin sets this manually, independent of <see cref="EventDto.StartsAt"/>/<see cref="EventDto.EndsAt"/>.</summary>
    Live,

    /// <summary>Automatic once a <see cref="Live"/> Event's Ends at elapses. Never applies to a <see cref="Draft"/> Event.</summary>
    Past,

    /// <summary>Manual, only reachable from <see cref="Live"/>, and terminal.</summary>
    Cancelled
}
