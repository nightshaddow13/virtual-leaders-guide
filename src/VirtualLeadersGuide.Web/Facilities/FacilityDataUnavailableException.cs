namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>
/// Thrown when a call to Api's <c>/api/facilities</c> or <c>/api/facilityTypes</c> resource fails at the
/// transport level, or returns a status <see cref="ApiFacilityClient"/>/<see cref="ApiFacilityTypeClient"/>
/// doesn't otherwise handle.
/// </summary>
/// <remarks>
/// Shared by both clients in this folder, mirroring <c>Activities.ActivityDataUnavailableException</c>'s
/// role for the Activity path - deliberately not swallowed into an empty result, so a caller fails loudly
/// instead of rendering as if the store returned nothing.
/// </remarks>
public sealed class FacilityDataUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
