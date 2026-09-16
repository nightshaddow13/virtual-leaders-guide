using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using Radzen.Blazor;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.InfoPages;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class InfoPageList
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    [Inject]
    private ApiInfoPageClient InfoPageClient { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    /// <remarks>
    /// Two icon-only row actions (Edit, Delete - ADR-0037), not <c>Dashboard.razor.cs</c>'s three: same
    /// per-button arithmetic its <c>ActionColumnWidth</c> remarks document (~44px per button plus a 4px gap
    /// between them), applied to two buttons instead of three - <c>76px</c> (one button, matching
    /// <c>Users.razor.cs</c>'s own <c>ActionColumnWidth</c>) plus one more 48px unit.
    /// </remarks>
    private const string ActionColumnWidth = "124px";

    [Parameter]
    public Guid EventId { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Ready }

    private PageState state = PageState.Loading;
    private RadzenDataGrid<InfoPageDto>? grid;
    private string? eventName;
    private bool isAdmin;

    /// <remarks>Must start <see langword="null"/> - see <c>Dashboard.razor.cs</c>'s identically-reasoned <c>events</c> field.</remarks>
    private IEnumerable<InfoPageDto>? infoPages;

    private int totalCount;
    private bool isLoading;
    private string? loadErrorMessage;
    private string? deleteErrorMessage;

    /// <remarks>
    /// Gates on <see cref="ApiEventClient.GetEventAsync"/>, not a new check - <c>InfoPageResourceDefinition</c>'s
    /// collection filter silently narrows an unassigned Director's request to an empty page rather than
    /// 403ing (ADR-0059), so listing InfoPages alone can't distinguish "not assigned to this Event" from "no
    /// InfoPages yet." <see cref="ApiEventClient.GetEventAsync"/>'s own 403 (ADR-0031) is the real gate, and
    /// its <see cref="EventDto.Name"/> supplies the breadcrumb for free - the same pattern
    /// <c>EventEditor.razor.cs</c> already establishes (the API's 403 is the gate; <see cref="EventAccessView"/>
    /// is only ever a rendering hint, here used just to word the breadcrumb's first segment the same way
    /// <c>Dashboard.razor</c> already does, not to branch the InfoPage list's own shape - ADR-0059 makes an
    /// assigned Director's InfoPage access identical to an Admin's).
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

        try
        {
            (IReadOnlyList<InfoPageDto> pageInfoPages, int total) =
                await InfoPageClient.GetInfoPagesForEventAsync(EventId, pageNumber, pageSize, CancellationToken.None);

            infoPages = pageInfoPages;
            totalCount = total;
        }
        catch (InfoPageDataUnavailableException)
        {
            infoPages = [];
            totalCount = 0;
            loadErrorMessage = "Something went wrong loading Info pages. Try refreshing the page.";
        }

        isLoading = false;
    }

    private async Task DeleteAsync(InfoPageDto item)
    {
        deleteErrorMessage = null;

        var parameters = InfoPageDeleteConfirmation.BuildDialogParameters(item.Title);
        bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>("Delete info page?", parameters);
        if (confirmed is not true)
        {
            return;
        }

        try
        {
            InfoPageWriteOutcome outcome = await InfoPageClient.DeleteAsync(item.Id, CancellationToken.None);
            if (outcome is InfoPageWriteOutcome.Success or InfoPageWriteOutcome.NotFound)
            {
                await grid!.Reload();
            }
            else if (outcome == InfoPageWriteOutcome.Forbidden)
            {
                deleteErrorMessage = "You don't have permission to delete this Info page.";
            }
        }
        catch (InfoPageDataUnavailableException)
        {
            deleteErrorMessage = "Something went wrong deleting this Info page. Try again.";
        }
    }

    private void NavigateToDashboard() => NavigationManager.NavigateTo("dashboard", forceLoad: true);
}
