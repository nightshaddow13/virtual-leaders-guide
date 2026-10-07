using System.Net;
using VirtualLeadersGuide.Web.Identity;
using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>Thin HTTP client over Api's <c>/api/facilities</c> JSON:API resource (P8-2, #166).</summary>
/// <remarks>
/// Mirrors <c>Activities.ApiActivityClient</c>'s shape - typed outcomes for expected non-2xx responses
/// rather than exceptions, <see cref="FacilityDataUnavailableException"/> for everything else. Requests go
/// through <see cref="InternalApiClient"/>, not a bare <c>IHttpClientFactory.CreateClient("Api")</c>,
/// because <c>/api/*</c> requires the internal JWT that only <see cref="InternalApiClient"/> attaches. The
/// read/update/delete methods <c>FacilityResourceDefinition</c> already authorizes on the Api side arrive as
/// their own UI needs them - P8-3 (#167) adds the list read; P8-4/P8-5 (#168/#169) still owe update/delete.
/// Request plumbing shared with <see cref="ApiFacilityTypeClient"/> lives in
/// <see cref="FacilityApiClientBase"/>.
/// </remarks>
public sealed class ApiFacilityClient(InternalApiClient apiClient) : FacilityApiClientBase(apiClient, "Facility")
{
    private const string FacilitiesPath = "/api/facilities";
    private const string ResourceType = "facilities";

    /// <summary>Lists every Facility, sorted by Name ascending unless <paramref name="sort"/> says otherwise.</summary>
    /// <param name="pageNumber">The 1-based page to fetch.</param>
    /// <param name="pageSize">The number of Facilities per page.</param>
    /// <param name="sort">
    /// A JSON:API <c>sort=</c> value (e.g. <c>name</c> or <c>-name</c>, see <c>JsonApi.JsonApiSort</c>), or
    /// <see langword="null"/> to fall back to <c>name</c> ascending - the wireframe's drawn order.
    /// </param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="FacilityReadOutcome.Success"/> with the page of Facilities and the total count across all
    /// pages; or <see cref="FacilityReadOutcome.Forbidden"/> if the caller holds no recognized role claim at
    /// all. Unlike <c>ApiActivityClient.GetActivitiesForEventAsync</c>, Forbidden is reachable here - a
    /// Facility has no owning Event to narrow a collection against, so <c>FacilityResourceDefinition</c>
    /// denies outright rather than silently returning an empty page (ADR-0070).
    /// </returns>
    public async Task<(FacilityReadOutcome Outcome, IReadOnlyList<FacilityDto> Facilities, int Total)> ListAsync(
        int pageNumber, int pageSize, string? sort, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, BuildCollectionUri(pageNumber, pageSize, sort));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (FacilityReadOutcome.Forbidden, [], 0);
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        FacilityCollectionDocument document = await ReadAsync<FacilityCollectionDocument>(response, cancellationToken);
        var facilities = document.Data.Select(ToDto).ToList();
        return (FacilityReadOutcome.Success, facilities, document.Meta?.Total ?? facilities.Count);
    }

    /// <summary>Creates a new Facility tagged with the given Facility Type.</summary>
    /// <param name="name">The Facility's display Name.</param>
    /// <param name="facilityTypeId">The Facility Type to tag it with - resolved or just created by the caller.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="FacilityWriteOutcome.Success"/> with the created Facility;
    /// <see cref="FacilityWriteOutcome.Forbidden"/> if the caller isn't an Admin (ADR-0070); or
    /// <see cref="FacilityWriteOutcome.Invalid"/> with the offending pointer if
    /// <paramref name="facilityTypeId"/> doesn't exist.
    /// </returns>
    public async Task<(FacilityWriteOutcome Outcome, FacilityDto? Facility, IReadOnlyList<string> Pointers)> CreateAsync(
        string name, Guid facilityTypeId, CancellationToken cancellationToken)
    {
        var body = new FacilityDocument
        {
            Data = new FacilityResourceObject
            {
                Type = ResourceType,
                Attributes = new FacilityAttributesDto { Name = name, FacilityTypeId = facilityTypeId }
            }
        };
        using var request = NewRequest(HttpMethod.Post, FacilitiesPath, body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (FacilityWriteOutcome.Forbidden, null, []);
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return (FacilityWriteOutcome.Invalid, null, await ReadErrorPointersAsync(response, cancellationToken));
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        FacilityDocument created = await ReadAsync<FacilityDocument>(response, cancellationToken);
        return (FacilityWriteOutcome.Success, ToDto(created.Data), []);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorPointersAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ErrorDocument document = await ReadAsync<ErrorDocument>(response, cancellationToken);
        return [.. document.Errors.Select(error => error.Source?.Pointer).OfType<string>()];
    }

    private static FacilityDto ToDto(FacilityResourceObject resource) => new()
    {
        Id = Guid.Parse(resource.Id!),
        Name = resource.Attributes!.Name!,
        FacilityTypeId = resource.Attributes.FacilityTypeId!.Value
    };

    /// <remarks>
    /// No <c>filter=</c> clause, unlike <c>ApiActivityClient.BuildCollectionUri</c> - a Facility carries no
    /// <c>EventId</c> to filter on (ADR-0066), so every page is drawn from the same unscoped collection.
    /// </remarks>
    private static string BuildCollectionUri(int pageNumber, int pageSize, string? sort) =>
        $"{FacilitiesPath}?sort={sort ?? "name"}&page[number]={pageNumber}&page[size]={pageSize}";
}
