using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using Radzen.Blazor;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Facilities;
using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <remarks>
/// Admin-only, despite <c>FacilityAccessPolicy.CanRead</c> being open to any signed-in Admin or Director
/// (ADR-0070) - the gate here is this page's own choice, narrower than its Api, because
/// <c>FacilityTypeResourceDefinition</c> stays Admin-only on every verb (ADR-0071): a Director could read
/// every Facility row but never resolve a single Type name, which this grid's Type column needs. See
/// ADR-0070's amendment for the full reasoning. The row-action column holds one icon-only Edit button
/// (ADR-0037) and nothing else yet: the Name stays plain text until P9's detail page exists to link to
/// (ADR-0073), and P8-5 (#169) adds Delete beside Edit.
/// </remarks>
public partial class FacilityList
{
    /// <remarks>
    /// One icon-only button, so the same width <c>Users.razor.cs</c> uses for its own single-button column -
    /// <c>RadzenDataGrid</c>'s fixed table layout clips a column too narrow for its content rather than growing
    /// it (ADR-0037). P8-5's second button will need the two-button width <c>InfoPageList.razor.cs</c> documents.
    /// </remarks>
    private const string ActionColumnWidth = "76px";

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiFacilityClient FacilityClient { get; set; } = default!;

    [Inject]
    private ApiFacilityTypeClient FacilityTypeClient { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Ready }

    private PageState state = PageState.Loading;
    private RadzenDataGrid<FacilityDto>? grid;
    private Dictionary<Guid, string> typeNamesById = new();

    /// <remarks>Must start <see langword="null"/> - see <c>Dashboard.razor.cs</c>'s identically-reasoned <c>events</c> field.</remarks>
    private IEnumerable<FacilityDto>? facilities;

    private int totalCount;
    private bool isLoading;
    private string? loadErrorMessage;

    /// <remarks>
    /// The same flat <c>EventAccessView.IsAdmin</c> gate <c>FacilityEditor.razor.cs</c>/<c>Users.razor.cs</c>
    /// use - this page has no route parameter to resolve first, so the gate runs in
    /// <see cref="OnInitializedAsync"/> rather than <c>OnParametersSetAsync</c>. Once past the gate, pre-loads
    /// every Facility Type via <see cref="ApiFacilityTypeClient.ListAsync"/> - the Type column's data source -
    /// the same pre-load <c>FacilityEditor.razor.cs</c> already does for its own autocomplete, rather than
    /// resolving each row's Type lazily.
    /// </remarks>
    protected override async Task OnInitializedAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState authState = await AuthenticationStateTask;
        if (!new EventAccessView(authState.User).IsAdmin)
        {
            state = PageState.Denied;
            return;
        }

        try
        {
            (FacilityTypeReadOutcome outcome, IReadOnlyList<FacilityTypeDto> types) =
                await FacilityTypeClient.ListAsync(CancellationToken.None);

            if (outcome != FacilityTypeReadOutcome.Success)
            {
                state = PageState.Denied;
                return;
            }

            typeNamesById = types.ToDictionary(type => type.Id, type => type.Name);
        }
        catch (FacilityDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        state = PageState.Ready;
    }

    /// <remarks>Wrapped in try/catch - an uncaught exception out of a Radzen callback crashes the whole circuit (ADR-0036's neighboring pages document this same hazard).</remarks>
    private async Task LoadDataAsync(LoadDataArgs args)
    {
        isLoading = true;
        loadErrorMessage = null;

        int pageSize = args.Top ?? 10;
        int pageNumber = (args.Skip ?? 0) / pageSize + 1;
        string? sort = JsonApiSort.ToJsonApiSort(args.Sorts);

        try
        {
            (FacilityReadOutcome outcome, IReadOnlyList<FacilityDto> pageFacilities, int total) =
                await FacilityClient.ListAsync(pageNumber, pageSize, sort, CancellationToken.None);

            if (outcome == FacilityReadOutcome.Forbidden)
            {
                state = PageState.Denied;
                return;
            }

            facilities = pageFacilities;
            totalCount = total;
        }
        catch (FacilityDataUnavailableException)
        {
            facilities = [];
            totalCount = 0;
            loadErrorMessage = "Something went wrong loading Facilities. Try refreshing the page.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private string TypeName(FacilityDto facility) =>
        typeNamesById.GetValueOrDefault(facility.FacilityTypeId, "—");
}
