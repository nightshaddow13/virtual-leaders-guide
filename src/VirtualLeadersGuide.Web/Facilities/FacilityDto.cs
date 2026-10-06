namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>The dashboard's view of a single Facility, mapped from Api's <c>/api/facilities</c> resource (P8-2, #166).</summary>
public sealed class FacilityDto
{
    /// <summary>This Facility's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>This Facility's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The Facility Type this Facility is tagged with - unlike <c>Activity.EventId</c>, this FK can change (a later story edits it).</summary>
    public required Guid FacilityTypeId { get; init; }
}
