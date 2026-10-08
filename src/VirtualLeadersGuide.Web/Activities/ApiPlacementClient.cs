using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;
using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Activities;

/// <summary>Thin HTTP client over Api's Tier (<c>/api/tabs</c>, <c>/api/subTabs</c>, <c>/api/sections</c>, <c>/api/subSections</c>) and <c>/api/placements</c> JSON:API resources (P5-11, #96).</summary>
/// <remarks>
/// Mirrors <see cref="ApiActivityClient"/>'s shape - typed outcomes for expected non-2xx responses,
/// <see cref="ActivityDataUnavailableException"/> for everything else (Placement is part of the same
/// Activity feature area, so it shares that exception rather than minting a near-identical one). The Tier
/// resources are read-only (ADR-0072): this client never creates or deletes a Tier directly, it only ever
/// sends a Placement carrying a Tier's <em>name</em> and lets Api resolve-or-create it.
/// </remarks>
public sealed class ApiPlacementClient(InternalApiClient apiClient)
{
    private const string JsonApiMediaType = "application/vnd.api+json";
    private const string PlacementsPath = "/api/placements";
    private const string PlacementsResourceType = "placements";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Loads one Event's whole Tier structure and every Placement on it - the data behind the Activity page's live tree and its builder's autofill (decision 1 of the plan: one eager load, not a request per level as the user types).</summary>
    /// <param name="eventId">The Event whose Tiers and Placements to load.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP calls.</param>
    /// <returns>
    /// The tree. Never forbidden: an unassigned Director's requests are silently narrowed to nothing by Api
    /// (the same posture <see cref="ApiActivityClient.GetActivitiesForEventAsync"/> documents) - callers gate
    /// on <see cref="ApiEventClient"/>'s own read first.
    /// </returns>
    public async Task<PlacementTreeDto> GetTreeForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        Task<List<TierResourceObject<TierAttributesDto>>> tabs = ListAsync<TierAttributesDto>("/api/tabs", eventId, cancellationToken);
        Task<List<TierResourceObject<TierAttributesDto>>> subTabs = ListAsync<TierAttributesDto>("/api/subTabs", eventId, cancellationToken);
        Task<List<TierResourceObject<TierAttributesDto>>> sections = ListAsync<TierAttributesDto>("/api/sections", eventId, cancellationToken);
        Task<List<TierResourceObject<TierAttributesDto>>> subSections = ListAsync<TierAttributesDto>("/api/subSections", eventId, cancellationToken);
        Task<List<TierResourceObject<PlacementAttributesDto>>> placements = ListAsync<PlacementAttributesDto>(PlacementsPath, eventId, cancellationToken);
        await Task.WhenAll(tabs, subTabs, sections, subSections, placements);

        return new PlacementTreeDto
        {
            Tabs = [.. tabs.Result.Select(r => new TabDto
            {
                Id = Guid.Parse(r.Id!), Name = r.Attributes!.Name!, SortOrder = r.Attributes.SortOrder ?? 0
            })],
            SubTabs = [.. subTabs.Result.Select(r => new SubTabDto
            {
                Id = Guid.Parse(r.Id!), TabId = r.Attributes!.TabId!.Value, Name = r.Attributes.Name!, SortOrder = r.Attributes.SortOrder ?? 0
            })],
            Sections = [.. sections.Result.Select(r => new SectionDto
            {
                Id = Guid.Parse(r.Id!), ParentTabId = r.Attributes!.ParentTabId, ParentSubTabId = r.Attributes.ParentSubTabId,
                Name = r.Attributes.Name!, SortOrder = r.Attributes.SortOrder ?? 0
            })],
            SubSections = [.. subSections.Result.Select(r => new SubSectionDto
            {
                Id = Guid.Parse(r.Id!), SectionId = r.Attributes!.SectionId!.Value, Name = r.Attributes.Name!, SortOrder = r.Attributes.SortOrder ?? 0
            })],
            Placements = [.. placements.Result.Select(ToPlacementDto)]
        };
    }

    /// <summary>Creates one Placement for <paramref name="activityId"/> at the named Tier path.</summary>
    /// <param name="activityId">The Activity being placed.</param>
    /// <param name="tabName">The Tab's name - required; an existing Tab of this name (case-insensitively) is reused, otherwise one is created (ADR-0072).</param>
    /// <param name="subTabName">The Sub Tab's name, or <see langword="null"/> for none.</param>
    /// <param name="sectionName">The Section's name, or <see langword="null"/> for none.</param>
    /// <param name="subSectionName">The Sub Section's name, or <see langword="null"/> for none - requires <paramref name="sectionName"/>.</param>
    /// <param name="cancellationToken">Propagated to the underlying HTTP call.</param>
    /// <returns>
    /// <see cref="PlacementWriteOutcome.Success"/> with the created Placement;
    /// <see cref="PlacementWriteOutcome.Forbidden"/>; <see cref="PlacementWriteOutcome.Invalid"/> with the
    /// offending pointers; or <see cref="PlacementWriteOutcome.Conflict"/> if the Activity is already placed
    /// at this exact path.
    /// </returns>
    public async Task<(PlacementWriteOutcome Outcome, PlacementDto? Placement, IReadOnlyList<string> Pointers)> CreateAsync(
        Guid activityId, string tabName, string? subTabName, string? sectionName, string? subSectionName,
        CancellationToken cancellationToken)
    {
        var body = new TierDocument<PlacementAttributesDto>
        {
            Data = new TierResourceObject<PlacementAttributesDto>
            {
                Type = PlacementsResourceType,
                Attributes = new PlacementAttributesDto
                {
                    ActivityId = activityId,
                    TabName = tabName,
                    SubTabName = subTabName,
                    SectionName = sectionName,
                    SubSectionName = subSectionName
                }
            }
        };
        using var request = NewRequest(HttpMethod.Post, PlacementsPath, body);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                return (PlacementWriteOutcome.Forbidden, null, []);
            case HttpStatusCode.Conflict:
                return (PlacementWriteOutcome.Conflict, null, []);
            case HttpStatusCode.UnprocessableEntity:
                return (PlacementWriteOutcome.Invalid, null, await ReadErrorPointersAsync(response, cancellationToken));
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        var created = await ReadAsync<TierDocument<PlacementAttributesDto>>(response, cancellationToken);
        return (PlacementWriteOutcome.Success, ToPlacementDto(created.Data), []);
    }

    private async Task<List<TierResourceObject<TAttributes>>> ListAsync<TAttributes>(
        string path, Guid eventId, CancellationToken cancellationToken)
    {
        string filter = Uri.EscapeDataString($"equals(eventId,'{eventId}')");
        using var request = NewRequest(HttpMethod.Get, $"{path}?filter={filter}&page[size]=9999");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        return (await ReadAsync<TierCollectionDocument<TAttributes>>(response, cancellationToken)).Data;
    }

    private static PlacementDto ToPlacementDto(TierResourceObject<PlacementAttributesDto> resource) => new()
    {
        Id = Guid.Parse(resource.Id!),
        ActivityId = resource.Attributes!.ActivityId!.Value,
        TabId = resource.Attributes.TabId!.Value,
        SubTabId = resource.Attributes.SubTabId,
        SectionId = resource.Attributes.SectionId,
        SubSectionId = resource.Attributes.SubSectionId,
        SortOrder = resource.Attributes.SortOrder ?? 0
    };

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri) =>
        NewRequest<object>(method, uri, null);

    private static HttpRequestMessage NewRequest<TBody>(HttpMethod method, string uri, TBody? body)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiMediaType));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiMediaType);
        }

        return request;
    }

    /// <remarks>Same transport-failure discipline as <see cref="ApiActivityClient"/>'s own <c>SendAsync</c> - see its remarks.</remarks>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not AuthorizationDataUnavailableException && !cancellationToken.IsCancellationRequested)
        {
            throw new ActivityDataUnavailableException("The Placement store (Api) is unreachable.", ex);
        }
    }

    private static void EnsureExpectedStatus(HttpResponseMessage response, params ReadOnlySpan<HttpStatusCode> expected)
    {
        if (!expected.Contains(response.StatusCode))
        {
            throw new ActivityDataUnavailableException(
                $"The Placement store (Api) returned an unexpected {(int)response.StatusCode} response.",
                new HttpRequestException(response.ReasonPhrase));
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken))!;

    private static async Task<IReadOnlyList<string>> ReadErrorPointersAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ErrorDocument document = await ReadAsync<ErrorDocument>(response, cancellationToken);
        return [.. document.Errors.Select(error => error.Source?.Pointer).OfType<string>()];
    }
}
