namespace VirtualLeadersGuide.Web.JsonApi;

/// <summary>
/// The JSON:API envelope shapes shared by every <c>Api*Client</c> in this app (<c>ApiEventClient</c>,
/// <c>ApiDirectorClient</c>, <c>ApiInfoPageClient</c>) - <see langword="internal"/> wire-format detail, not
/// exposed past those clients. Each feature area still declares its own resource-specific envelope types
/// (<c>EventDocument</c>, <c>EventCollectionDocument</c>, etc., alongside its own <c>*AttributesDto</c>) in
/// its own <c>JsonApiDocument.cs</c> - only the resource-shaped-nothing plumbing lives here, consolidated
/// after it had drifted into three near-identical copies (P5-17, #22 code review).
/// </summary>
/// <remarks>
/// Populated only when Api's <c>IncludeTotalResourceCount</c> option is on (it is, as of P2-9).
/// </remarks>
internal sealed class DocumentMeta
{
    public int? Total { get; init; }
}

/// <summary>The response body for a non-2xx JSON:API error response.</summary>
internal sealed class ErrorDocument
{
    public required List<ErrorObject> Errors { get; init; }
}

/// <summary>
/// One JSON:API error. <see cref="Source"/> is what a caller needing a field-level pointer
/// (<c>ApiEventClient</c>, <c>ApiInfoPageClient</c>) reads; <c>ApiDirectorClient</c> doesn't need it today
/// (its one <c>Conflict</c> case, an already-granted Director, carries no field to point at) but the shape
/// is shared rather than split, since the alternative is re-splitting it the next time a Directors-area call
/// does need a pointer.
/// </summary>
internal sealed class ErrorObject
{
    public string? Title { get; init; }

    public string? Detail { get; init; }

    public ErrorSource? Source { get; init; }
}

/// <summary>Where in the request body an <see cref="ErrorObject"/> originates.</summary>
/// <remarks>
/// <see cref="Pointer"/> is a JSON Pointer into the request body (e.g. <c>/data/attributes/name</c>) - a
/// caller surfaces these directly to route a 409/422 to the offending form field, matching each resource
/// definition's own error-pointer convention on the Api side.
/// </remarks>
internal sealed class ErrorSource
{
    public string? Pointer { get; init; }
}
