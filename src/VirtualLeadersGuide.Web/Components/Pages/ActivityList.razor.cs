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
/// Deliberately Name-only this slice - no "Placed under" chip column and no row-action column.
/// <c>Placement</c>/<c>Tab</c>/<c>SubTab</c>/<c>Section</c>/<c>SubSection</c> don't exist yet; that schema
/// and the chip-per-Placement column it would render both land in P5-11 (#96), which depends on this story
/// (see the comments left on #93/#96 recording the moved criterion). Row actions (edit/delete) wait on P5-8
/// (#94)/P5-9 (#95), which own the pages those icons would navigate to - nothing ships disabled or dead in
/// the meantime. The empty state is a <c>RadzenCard</c> via <c>&lt;EmptyTemplate&gt;</c>, not the
/// <c>EmptyText</c> string <c>InfoPageList</c>/<c>Dashboard</c>/<c>Users</c> all use - wireframe turn 1a
/// explicitly overrides that convention ("the copy has to teach the inline-create model"), though the
/// shipped copy here is trimmed to this slice (no Tab/Category-creation sentence, since nothing in this
/// story lets you type one yet).
/// </remarks>
public partial class ActivityList
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    [Inject]
    private ApiActivityClient ActivityClient { get; set; } = default!;

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

    private void NavigateToDashboard() => NavigationManager.NavigateTo("dashboard", forceLoad: true);
}
