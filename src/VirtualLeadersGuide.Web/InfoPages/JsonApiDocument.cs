namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>
/// One of the minimal JSON:API envelope shapes <see cref="ApiInfoPageClient"/> sends to and reads from
/// <c>/api/infoPages</c> - <see langword="internal"/> wire-format detail, not exposed past this client;
/// callers see <see cref="InfoPageDto"/> and the outcome enums instead.
/// </summary>
internal sealed class InfoPageResourceObject
{
    public required string Type { get; init; }

    public string? Id { get; init; }

    public InfoPageAttributesDto? Attributes { get; init; }
}

/// <summary>An InfoPage's <c>eventId</c>/<c>title</c>/<c>markdownContent</c> attributes, as sent or received in a request/response.</summary>
/// <remarks>
/// Every property is nullable so a request can omit an attribute rather than send it as JSON <c>null</c> -
/// matching <c>Events.EventAttributesDto</c>'s discipline. <see cref="EventId"/> only ever appears on a
/// create request - <c>Page.EventId</c> carries no <c>AllowChange</c>, so an update never sends it.
/// </remarks>
internal sealed class InfoPageAttributesDto
{
    public Guid? EventId { get; init; }

    public string? Title { get; init; }

    public string? MarkdownContent { get; init; }
}

/// <summary>A single-resource JSON:API document - the request body for POST/PATCH and the response body for GET-single/POST.</summary>
internal sealed class InfoPageDocument
{
    public required InfoPageResourceObject Data { get; init; }
}

/// <summary>The response body for <c>GET /api/infoPages</c>.</summary>
internal sealed class InfoPageCollectionDocument
{
    public required List<InfoPageResourceObject> Data { get; init; }

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

/// <summary>One JSON:API error - see <see cref="ErrorSource.Pointer"/> for the part <see cref="ApiInfoPageClient"/> uses.</summary>
internal sealed class ErrorObject
{
    public string? Title { get; init; }

    public string? Detail { get; init; }

    public ErrorSource? Source { get; init; }
}

/// <summary>Where in the request body an <see cref="ErrorObject"/> originates.</summary>
/// <remarks>
/// <see cref="Pointer"/> is a JSON Pointer into the request body (e.g. <c>/data/attributes/eventId</c>) -
/// <see cref="ApiInfoPageClient"/> surfaces these directly, matching <c>EventResourceDefinition</c>'s own
/// error-pointer convention.
/// </remarks>
internal sealed class ErrorSource
{
    public string? Pointer { get; init; }
}
