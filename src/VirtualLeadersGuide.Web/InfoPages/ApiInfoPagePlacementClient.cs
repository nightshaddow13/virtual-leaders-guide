using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;
using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>Thin HTTP client over Api's <c>/api/infoPagePlacements</c> writes (P5-24, #208). Reads come with the Event's tree, <see cref="ApiPlacementClient.GetTreeForEventAsync"/>.</summary>
/// <remarks>Typed outcomes for expected non-2xx responses, <see cref="InfoPageDataUnavailableException"/> for everything else.</remarks>
public sealed class ApiInfoPagePlacementClient(InternalApiClient apiClient)
{
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const string PlacementsPath = "/api/infoPagePlacements";
    private const string ResourceType = "infoPagePlacements";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Places an InfoPage on a Tab and optional Sub Tab, by name (ADR-0072).</summary>
    /// <param name="infoPageId">The InfoPage to place.</param>
    /// <param name="tabName">The Tab's name.</param>
    /// <param name="subTabName">The Sub Tab's name, or <see langword="null"/> for the bare Tab.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>How Api answered.</returns>
    public Task<InfoPagePlacementWriteOutcome> CreateAsync(
        Guid infoPageId, string tabName, string? subTabName, CancellationToken cancellationToken) =>
        WriteAsync(
            HttpMethod.Post, PlacementsPath, id: null,
            new InfoPagePlacementAttributesDto { InfoPageId = infoPageId, TabName = tabName, SubTabName = subTabName },
            cancellationToken);

    /// <summary>Moves an existing Placement in one write, so a rejected move leaves it where it was.</summary>
    /// <param name="placementId">The Placement to move.</param>
    /// <param name="tabName">The new Tab's name.</param>
    /// <param name="subTabName">The new Sub Tab's name, or <see langword="null"/> for the bare Tab.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>How Api answered.</returns>
    public Task<InfoPagePlacementWriteOutcome> MoveAsync(
        Guid placementId, string tabName, string? subTabName, CancellationToken cancellationToken) =>
        WriteAsync(
            HttpMethod.Patch, $"{PlacementsPath}/{placementId}", placementId.ToString(),
            new InfoPagePlacementAttributesDto { TabName = tabName, SubTabName = subTabName },
            cancellationToken);

    /// <summary>Removes a Placement; Api also deletes any Tier it alone kept alive.</summary>
    /// <param name="placementId">The Placement to remove.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>How Api answered.</returns>
    public async Task<InfoPagePlacementWriteOutcome> DeleteAsync(Guid placementId, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Delete, $"{PlacementsPath}/{placementId}");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await ToOutcomeAsync(response, cancellationToken);
    }

    private async Task<InfoPagePlacementWriteOutcome> WriteAsync(
        HttpMethod method, string uri, string? id, InfoPagePlacementAttributesDto attributes, CancellationToken cancellationToken)
    {
        using var request = NewRequest(method, uri);
        request.Content = JsonContent.Create(
            new TierDocument<InfoPagePlacementAttributesDto>
            {
                Data = new TierResourceObject<InfoPagePlacementAttributesDto> { Type = ResourceType, Id = id, Attributes = attributes }
            },
            options: JsonOptions);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await ToOutcomeAsync(response, cancellationToken);
    }

    private static async Task<InfoPagePlacementWriteOutcome> ToOutcomeAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent:
                return InfoPagePlacementWriteOutcome.Success;
            case HttpStatusCode.Forbidden:
                return InfoPagePlacementWriteOutcome.Forbidden;
            case HttpStatusCode.NotFound:
                return InfoPagePlacementWriteOutcome.NotFound;
            case HttpStatusCode.UnprocessableEntity:
                return InfoPagePlacementWriteOutcome.Invalid;
            case HttpStatusCode.Conflict:
                switch (await ReadErrorCodeAsync(response, cancellationToken))
                {
                    case PlacementErrorCodes.SlotHasActivities:
                        return InfoPagePlacementWriteOutcome.SlotHasActivities;
                    case PlacementErrorCodes.SlotHasOtherInfoPage:
                        return InfoPagePlacementWriteOutcome.SlotHasOtherInfoPage;
                    case PlacementErrorCodes.InfoPageAlreadyPlaced:
                        return InfoPagePlacementWriteOutcome.AlreadyPlaced;
                }

                break;
        }

        throw new InfoPageDataUnavailableException(
            $"The Placement store (Api) returned an unexpected {(int)response.StatusCode} response.",
            new HttpRequestException(response.ReasonPhrase));
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ErrorDocument? document = await response.Content.ReadFromJsonAsync<ErrorDocument>(JsonOptions, cancellationToken);
            return document?.Errors.FirstOrDefault()?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));
        return request;
    }

    /// <remarks>Same transport-failure discipline as <see cref="ApiInfoPageClient"/>.</remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not AuthorizationDataUnavailableException && !cancellationToken.IsCancellationRequested)
        {
            throw new InfoPageDataUnavailableException("The Placement store (Api) is unreachable.", ex);
        }
    }
}
