using System.Net;
using System.Net.Http.Json;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Lists and deletes Facilities/Facility Types directly against <c>Api</c>'s <c>/api/facilities</c>/
/// <c>/api/facilityTypes</c> JSON:API resources - for <see cref="E2ETestBase"/>'s tracked per-test cleanup
/// and <see cref="AspireE2EFixture"/>'s own run-end sweep (ADR-0039). Unlike <see cref="EventsApiClient"/>,
/// carries no <c>Create*Async</c> method - P8-2 (#166) retains zero fixture Facilities/Facility Types (no
/// row needs seeding before a test's own <see cref="Microsoft.Playwright.IPage"/> exists), so every one
/// this suite ever sees was made through the real UI.
/// </summary>
/// <remarks>
/// One class for both resources, not two - they're created together through the same
/// <c>FacilityEditor</c> form and torn down together, so splitting them would only duplicate this class's
/// request plumbing for no benefit. <c>/api/facilities</c>/<c>/api/facilityTypes</c> sit behind
/// <see cref="InternalJwtDefaults.PolicyName"/>, same as every other <c>/api/*</c> resource - see
/// <see cref="EventsApiClient"/>'s own remarks for why every request here carries an Admin bearer token via
/// <see cref="AdminJsonApiClientBase"/>.
/// </remarks>
public sealed class FacilitiesApiClient(HttpClient httpClient, string internalJwtSigningKey)
    : AdminJsonApiClientBase(httpClient, internalJwtSigningKey)
{
    private const string FacilitiesPath = "/api/facilities";
    private const string FacilityTypesPath = "/api/facilityTypes";

    /// <summary>
    /// Every Facility whose <c>Name</c> starts with <c>e2e-</c> (ADR-0039's discriminator) - a hand-made
    /// Facility never matches, by construction.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The id and name of every matching Facility, across all pages.</returns>
    /// <exception cref="InvalidOperationException">The list request did not succeed.</exception>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> ListE2EFacilitiesAsync(CancellationToken cancellationToken)
    {
        string uri = BuildUnpagedCollectionUri(FacilitiesPath, "startsWith(name,'e2e-')");
        using HttpRequestMessage request = NewRequest(HttpMethod.Get, uri);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync("list", "e2e- Facilities", response, cancellationToken);
        }

        FacilityCollectionDocument document =
            (await response.Content.ReadFromJsonAsync<FacilityCollectionDocument>(JsonOptions, cancellationToken))!;
        return [.. document.Data.Select(resource => (Guid.Parse(resource.Id!), resource.Attributes!.Name!))];
    }

    /// <summary>Deletes the Facility identified by <paramref name="id"/> - the cleanup half of Facility creation (ADR-0039).</summary>
    /// <param name="id">The Facility to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>Tolerates 404, matching <see cref="EventsApiClient.DeleteEventAsync"/>'s own reasoning.</remarks>
    /// <exception cref="InvalidOperationException">The delete request failed for a reason other than "already gone."</exception>
    public async Task DeleteFacilityAsync(Guid id, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = NewRequest(HttpMethod.Delete, $"{FacilitiesPath}/{id}");

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode is not HttpStatusCode.NotFound)
        {
            await ThrowForFailureAsync("delete", id.ToString(), response, cancellationToken);
        }
    }

    /// <summary>Every Facility Type whose <c>Name</c> starts with <c>e2e-</c> (ADR-0039's discriminator).</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The id and name of every matching Facility Type, across all pages.</returns>
    /// <exception cref="InvalidOperationException">The list request did not succeed.</exception>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> ListE2EFacilityTypesAsync(CancellationToken cancellationToken)
    {
        string uri = BuildUnpagedCollectionUri(FacilityTypesPath, "startsWith(name,'e2e-')");
        using HttpRequestMessage request = NewRequest(HttpMethod.Get, uri);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync("list", "e2e- Facility Types", response, cancellationToken);
        }

        FacilityCollectionDocument document =
            (await response.Content.ReadFromJsonAsync<FacilityCollectionDocument>(JsonOptions, cancellationToken))!;
        return [.. document.Data.Select(resource => (Guid.Parse(resource.Id!), resource.Attributes!.Name!))];
    }

    /// <summary>Deletes the Facility Type identified by <paramref name="id"/> - the cleanup half of Facility Type creation (ADR-0039).</summary>
    /// <param name="id">The Facility Type to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>
    /// Tolerates 404, matching <see cref="DeleteFacilityAsync"/>'s own reasoning. Every call site deletes
    /// Facilities before Facility Types (<see cref="DeleteFacilityAsync"/> first) - a Facility Type's FK is
    /// <c>DeleteBehavior.Restrict</c> (ADR-0071), so deleting one still referenced by an un-swept Facility
    /// would fail the delete outright rather than cascading.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The delete request failed for a reason other than "already gone."</exception>
    public async Task DeleteFacilityTypeAsync(Guid id, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = NewRequest(HttpMethod.Delete, $"{FacilityTypesPath}/{id}");

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode is not HttpStatusCode.NotFound)
        {
            await ThrowForFailureAsync("delete", id.ToString(), response, cancellationToken);
        }
    }

    /// <summary>Minimal JSON:API envelope shapes this class reads - shared by both resources, since both expose only a <c>name</c> attribute to this client.</summary>
    private sealed class FacilityCollectionDocument
    {
        public required List<FacilityResourceObject> Data { get; init; }
    }

    private sealed class FacilityResourceObject
    {
        public string? Id { get; init; }

        public FacilityAttributesDto? Attributes { get; init; }
    }

    private sealed class FacilityAttributesDto
    {
        public string? Name { get; init; }
    }
}
