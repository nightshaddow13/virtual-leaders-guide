using System.Net;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>Thin HTTP client over Api's <c>/api/facilityTypes</c> JSON:API resource (P8-2, #166).</summary>
/// <remarks>
/// Mirrors <c>Activities.ApiActivityClient</c>'s shape - typed outcomes for expected non-2xx responses
/// rather than exceptions, <see cref="FacilityDataUnavailableException"/> for everything else. Requests go
/// through <see cref="InternalApiClient"/>, not a bare <c>IHttpClientFactory.CreateClient("Api")</c>,
/// because <c>/api/*</c> requires the internal JWT that only <see cref="InternalApiClient"/> attaches. Used
/// only from the Admin-gated <c>FacilityEditor</c> page - every verb on this resource is Admin-only
/// (ADR-0071), unlike <see cref="ApiFacilityClient"/>'s broader Read audience. Request plumbing shared with
/// <see cref="ApiFacilityClient"/> lives in <see cref="FacilityApiClientBase"/>.
/// </remarks>
public sealed class ApiFacilityTypeClient(InternalApiClient apiClient) : FacilityApiClientBase(apiClient, "Facility Type")
{
    private const string FacilityTypesPath = "/api/facilityTypes";
    private const string ResourceType = "facilityTypes";

    /// <summary>Lists every Facility Type, sorted by Name ascending - the autofill's data source.</summary>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// Every existing Facility Type, or an empty list if <see cref="FacilityTypeReadOutcome.Forbidden"/> - the
    /// caller isn't an Admin (ADR-0071), which <c>FacilityEditor</c>'s own Admin gate should already have
    /// ruled out before this is ever called.
    /// </returns>
    public async Task<(FacilityTypeReadOutcome Outcome, IReadOnlyList<FacilityTypeDto> FacilityTypes)> ListAsync(
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, $"{FacilityTypesPath}?sort=name");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (FacilityTypeReadOutcome.Forbidden, []);
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        FacilityTypeCollectionDocument document = await ReadAsync<FacilityTypeCollectionDocument>(response, cancellationToken);
        return (FacilityTypeReadOutcome.Success, [.. document.Data.Select(ToDto)]);
    }

    /// <summary>Creates a new Facility Type - the autofill's "use as new" path.</summary>
    /// <param name="name">The new Facility Type's display name.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="FacilityTypeWriteOutcome.Success"/> with the created Facility Type;
    /// <see cref="FacilityTypeWriteOutcome.Forbidden"/> if the caller isn't an Admin; or
    /// <see cref="FacilityTypeWriteOutcome.Conflict"/> if <paramref name="name"/> is already taken.
    /// </returns>
    public async Task<(FacilityTypeWriteOutcome Outcome, FacilityTypeDto? FacilityType)> CreateAsync(
        string name, CancellationToken cancellationToken)
    {
        var body = new FacilityTypeDocument
        {
            Data = new FacilityTypeResourceObject
            {
                Type = ResourceType,
                Attributes = new FacilityTypeAttributesDto { Name = name }
            }
        };
        using var request = NewRequest(HttpMethod.Post, FacilityTypesPath, body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (FacilityTypeWriteOutcome.Forbidden, null);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (FacilityTypeWriteOutcome.Conflict, null);
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        FacilityTypeDocument created = await ReadAsync<FacilityTypeDocument>(response, cancellationToken);
        return (FacilityTypeWriteOutcome.Success, ToDto(created.Data));
    }

    private static FacilityTypeDto ToDto(FacilityTypeResourceObject resource) => new()
    {
        Id = Guid.Parse(resource.Id!),
        Name = resource.Attributes!.Name!
    };
}
