using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>
/// Resource-specific JSON:API envelope shapes <see cref="ApiInfoPageClient"/> sends to and reads from
/// <c>/api/infoPages</c> - <see langword="internal"/> wire-format detail, not exposed past this client;
/// callers see <see cref="InfoPageDto"/> and the outcome enums instead. The resource-shaped-nothing
/// envelope plumbing lives in <see cref="VirtualLeadersGuide.Web.JsonApi"/> instead, shared with
/// <c>Events</c>.
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
