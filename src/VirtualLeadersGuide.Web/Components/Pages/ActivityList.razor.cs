using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using Radzen.Blazor;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <remarks>
/// Since P5-11 (#96) a "Placed under" column renders one chip per Placement (the criterion #93 deferred to
/// that story, see the comments left on #93/#96) and the header counts Placements alongside Activities.
/// Row click opens the Activity's page (<c>ActivityDetail</c>), which hosts the Placement builder and tree -
/// the wireframe's "row click opens the detail form" - so there is still no row-action column; icon actions
/// (edit/delete) wait on P5-8 (#94)/P5-9 (#95). The empty state is a <c>RadzenCard</c> via
/// <c>&lt;EmptyTemplate&gt;</c>, not the <c>EmptyText</c> string <c>InfoPageList</c>/<c>Dashboard</c>/<c>Users</c>
/// all use - wireframe turn 1a explicitly overrides that convention ("the copy has to teach the
/// inline-create model").
/// </remarks>
public partial class ActivityList
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    [Inject]
    private ApiActivityClient ActivityClient { get; set; } = default!;

    [Inject]
    private ApiPlacementClient PlacementClient { get; set; } = default!;

    [Parameter]
    public Guid EventId { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Ready }

    private PageState state = PageState.Loading;
    private RadzenDataGrid<ActivityDto>? grid;
    private string? eventName;
    private bool isAdmin;

    /// <remarks>Must start <see langword="null"/> - see <c>Dashboard.razor.cs</c>'s identically-reasoned <c>events</c> field.</remarks>
    private IEnumerable<ActivityDto>? activities;

    private int totalCount;
    private int placementCount;
    private IReadOnlyDictionary<Guid, IReadOnlyList<PlacementPath>> pathsByActivity = new Dictionary<Guid, IReadOnlyList<PlacementPath>>();
    private bool isLoading;
    private string? loadErrorMessage;

    /// <remarks>
    /// Gates on <see cref="ApiEventClient.GetEventAsync"/>, not a new check - <c>ActivityResourceDefinition</c>'s
    /// collection filter silently narrows an unassigned Director's request to an empty page rather than
    /// 403ing (ADR-0069), so listing Activities alone can't distinguish "not assigned to this Event" from "no
    /// Activities yet." <see cref="ApiEventClient.GetEventAsync"/>'s own 403 (ADR-0031) is the real gate, and
    /// its <see cref="EventDto.Name"/> supplies the breadcrumb for free - the same pattern
    /// <c>InfoPageList.razor.cs</c>'s own remarks already establish (<see cref="EventAccessView"/> is only ever
    /// a rendering hint whose claims can lag a grant by a sign-in cycle, never the authority - Api's response
    /// is; here it's used just to word the breadcrumb's first segment, not to branch the Activity list's own
    /// shape, since ADR-0069 makes an assigned Director's Activity access identical to an Admin's).
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState authState = await AuthenticationStateTask;
        if (!authState.User.Claims.Any(c => c.Type == ClaimTypes.Role))
        {
            NavigationManager.NavigateTo("Account/NoAccess");
            return;
        }

        isAdmin = new EventAccessView(authState.User).IsAdmin;

        EventReadOutcome outcome;
        EventDto? dto;
        try
        {
            (outcome, dto) = await EventClient.GetEventAsync(EventId, CancellationToken.None);
        }
        catch (EventDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        if (outcome != EventReadOutcome.Success || dto is null)
        {
            state = PageState.Denied;
            return;
        }

        eventName = dto.Name;
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
            (IReadOnlyList<ActivityDto> pageActivities, int total) =
                await ActivityClient.GetActivitiesForEventAsync(EventId, pageNumber, pageSize, sort, CancellationToken.None);

            PlacementTreeDto tree = await PlacementClient.GetTreeForEventAsync(EventId, CancellationToken.None);
            pathsByActivity = tree.PathsByActivity();
            placementCount = tree.Placements.Count;

            activities = pageActivities;
            totalCount = total;
        }
        catch (ActivityDataUnavailableException)
        {
            activities = [];
            totalCount = 0;
            loadErrorMessage = "Something went wrong loading Activities. Try refreshing the page.";
        }

        isLoading = false;
    }

    private IEnumerable<string> ChipsFor(Guid activityId) =>
        pathsByActivity.TryGetValue(activityId, out IReadOnlyList<PlacementPath>? paths) ? paths.Select(p => p.Display) : [];

    private void OpenActivity(ActivityDto activity) =>
        NavigationManager.NavigateTo($"dashboard/events/{EventId}/activities/{activity.Id}");

    private void NavigateToDashboard() => NavigationManager.NavigateTo("dashboard", forceLoad: true);
}
