namespace VirtualLeadersGuide.Web.Events;

/// <summary>
/// One of the minimal JSON:API envelope shapes <see cref="ApiEventClient"/> sends to and reads from
/// <c>/api/events</c> - <see langword="internal"/> wire-format detail, not exposed past this client;
/// callers see <see cref="EventDto"/> and the outcome enums instead.
/// </summary>
internal sealed class EventResourceObject
{
    public required string Type { get; init; }

    public string? Id { get; init; }

    public EventAttributesDto? Attributes { get; init; }
}

/// <summary>An Event's <c>name</c>/<c>slug</c>/<c>passcode</c> attributes, as sent or received in a request/response.</summary>
/// <remarks>
/// Every property is nullable so a request can omit an attribute rather than send it as JSON <c>null</c> -
/// a PATCH that included <c>"slug": null</c> would try to null out a <c>NOT NULL</c> column (see
/// <c>Event.Slug</c>'s remarks in Api). <see cref="ApiEventClient"/> serializes with
/// <c>DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull</c> so an omitted value here is omitted
/// from the wire, not nulled.
/// </remarks>
internal sealed class EventAttributesDto
{
    public string? Name { get; init; }

    public string? Slug { get; init; }

    public string? Passcode { get; init; }
}

/// <summary>A single-resource JSON:API document - the request body for POST/PATCH and the response body for GET-single/POST.</summary>
internal sealed class EventDocument
{
    public required EventResourceObject Data { get; init; }
}

/// <summary>The response body for <c>GET /api/events</c>.</summary>
internal sealed class EventCollectionDocument
{
    public required List<EventResourceObject> Data { get; init; }

    public DocumentMeta? Meta { get; init; }
}

/// <summary>Top-level document metadata.</summary>
/// <remarks>Populated only when Api's <c>IncludeTotalResourceCount</c> option is on (it is, as of P2-9).</remarks>
internal sealed class DocumentMeta
{
    public int? Total { get; init; }
}

/// <summary>The response body for a non-2xx JSON:API error response.</summary>
internal sealed class ErrorDocument
{
    public required List<ErrorObject> Errors { get; init; }
}

/// <summary>One JSON:API error - see <see cref="ErrorSource.Pointer"/> for the part <see cref="ApiEventClient"/> uses.</summary>
internal sealed class ErrorObject
{
    public string? Title { get; init; }

    public string? Detail { get; init; }

    public ErrorSource? Source { get; init; }
}

/// <summary>Where in the request body an <see cref="ErrorObject"/> originates.</summary>
/// <remarks>
/// <see cref="Pointer"/> is a JSON Pointer into the request body (e.g. <c>/data/attributes/name</c>) -
/// <see cref="ApiEventClient"/> surfaces these directly so a caller can route a 409 to the offending form
/// field, matching <c>EventResourceDefinition.ConflictError</c> on the Api side.
/// </remarks>
internal sealed class ErrorSource
{
    public string? Pointer { get; init; }
}
