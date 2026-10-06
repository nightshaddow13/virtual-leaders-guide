using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>
/// Resource-specific JSON:API envelope shapes <see cref="ApiFacilityClient"/>/<see cref="ApiFacilityTypeClient"/>
/// send to and read from <c>/api/facilities</c>/<c>/api/facilityTypes</c> - <see langword="internal"/>
/// wire-format detail, not exposed past these clients; callers see <see cref="FacilityDto"/>/
/// <see cref="FacilityTypeDto"/> and the outcome enums instead. The resource-shaped-nothing envelope
/// plumbing lives in <see cref="VirtualLeadersGuide.Web.JsonApi"/> instead, shared with <c>Activities</c>/
/// <c>InfoPages</c>/<c>Events</c>.
/// </summary>
internal sealed class FacilityResourceObject
{
    /// <summary>The JSON:API resource type - always <c>"facilities"</c>.</summary>
    public required string Type { get; init; }

    /// <summary>This Facility's id - absent on a create request body, since the id is server-generated.</summary>
    public string? Id { get; init; }

    /// <summary>This Facility's attributes.</summary>
    public FacilityAttributesDto? Attributes { get; init; }
}

/// <summary>A Facility's <c>name</c>/<c>facilityTypeId</c> attributes, as sent or received in a request/response.</summary>
/// <remarks>
/// Every property is nullable so a request can omit an attribute rather than send it as JSON
/// <see langword="null"/> - matching <c>Activities.ActivityAttributesDto</c>'s discipline.
/// </remarks>
internal sealed class FacilityAttributesDto
{
    /// <summary>This Facility's display name.</summary>
    public string? Name { get; init; }

    /// <summary>The Facility Type this Facility is tagged with.</summary>
    public Guid? FacilityTypeId { get; init; }
}

/// <summary>A single-resource JSON:API document - the request body for POST and the response body for POST.</summary>
internal sealed class FacilityDocument
{
    /// <summary>The Facility resource object this document carries.</summary>
    public required FacilityResourceObject Data { get; init; }
}

/// <summary>The response body for <c>GET /api/facilities</c>.</summary>
internal sealed class FacilityCollectionDocument
{
    /// <summary>Every returned Facility resource object.</summary>
    public required List<FacilityResourceObject> Data { get; init; }

    /// <summary>The total count across all pages, when Api's <c>IncludeTotalResourceCount</c> option supplies one.</summary>
    public DocumentMeta? Meta { get; init; }
}

/// <summary>Resource-specific envelope for <c>/api/facilityTypes</c> - see <see cref="FacilityResourceObject"/>'s remarks.</summary>
internal sealed class FacilityTypeResourceObject
{
    /// <summary>The JSON:API resource type - always <c>"facilityTypes"</c>.</summary>
    public required string Type { get; init; }

    /// <summary>This Facility Type's id - absent on a create request body, since the id is server-generated.</summary>
    public string? Id { get; init; }

    /// <summary>This Facility Type's attributes.</summary>
    public FacilityTypeAttributesDto? Attributes { get; init; }
}

/// <summary>A Facility Type's <c>name</c> attribute, as sent or received in a request/response.</summary>
internal sealed class FacilityTypeAttributesDto
{
    /// <summary>This Facility Type's display name.</summary>
    public string? Name { get; init; }
}

/// <summary>A single-resource JSON:API document - the request body for POST and the response body for POST.</summary>
internal sealed class FacilityTypeDocument
{
    /// <summary>The Facility Type resource object this document carries.</summary>
    public required FacilityTypeResourceObject Data { get; init; }
}

/// <summary>The response body for <c>GET /api/facilityTypes</c>.</summary>
internal sealed class FacilityTypeCollectionDocument
{
    /// <summary>Every returned Facility Type resource object.</summary>
    public required List<FacilityTypeResourceObject> Data { get; init; }
}
