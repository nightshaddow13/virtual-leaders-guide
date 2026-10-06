namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>The dashboard's view of a single Facility Type, mapped from Api's <c>/api/facilityTypes</c> resource (P8-2, #166).</summary>
public sealed class FacilityTypeDto
{
    /// <summary>This Facility Type's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>This Facility Type's display name.</summary>
    public required string Name { get; init; }
}
