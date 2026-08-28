using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using Radzen.Blazor;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class Dashboard
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    /// <remarks>
    /// <see cref="ClaimTypes.Role"/> claims come from <c>ApplicationUserClaimsPrincipalFactory</c> (P2-5,
    /// #14), stamped at sign-in from P2-3's <c>UserRole</c> grants. A user holding no grant still redirects
    /// to <c>NoAccess</c>. <see cref="EventAccessView"/> then decides Admin vs. Director rendering from the
    /// same claims - a hint the grid honors, not the authority (ADR-0031); <see cref="ApiEventClient"/>'s
    /// collection call is itself scoped server-side regardless of what this page assumes.
    /// </remarks>
    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    /// <remarks>
    /// Not the icon button's own ~44px: the grid renders with a fixed table layout
    /// (<c>rz-grid-table-fixed</c>), so a column can never grow to fit its content - one narrower than the
    /// button clips it against the cell's padding box instead of just looking cramped. 76px leaves the
    /// centered button real breathing room on both sides. The button itself is icon-only, not text - see
    /// ADR-0037 for the row-action convention this establishes.
    /// </remarks>
    private const string ActionColumnWidth = "76px";

    private RadzenDataGrid<EventDto>? grid;
    private EventAccessView? accessView;

    /// <remarks>
    /// Must start <see langword="null"/>, not an empty collection - <c>RadzenDataGrid</c> only invokes
    /// <see cref="LoadDataAsync"/> automatically on first render when its bound <c>Data</c> is
    /// <see langword="null"/>; a non-null (even empty) initial value reads as "the caller already supplied
    /// the page's data" and the grid never calls back at all, leaving it permanently empty regardless of what
    /// <see cref="ApiEventClient"/> actually holds.
    /// </remarks>
    private IEnumerable<EventDto>? events;

    private int totalCount;
    private bool isLoading;
    private string? loadErrorMessage;

    protected override async Task OnInitializedAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState state = await AuthenticationStateTask;
        bool hasAnyRole = state.User.Claims.Any(c => c.Type == ClaimTypes.Role);

        if (!hasAnyRole)
        {
            NavigationManager.NavigateTo("Account/NoAccess");
            return;
        }

        accessView = new EventAccessView(state.User);
    }

    /// <remarks>
    /// Wrapped in try/catch deliberately: an uncaught exception out of a <c>RadzenDataGrid</c> event
    /// callback crashes the whole Blazor Server circuit (there's no per-request failure page to fall back
    /// to under <c>InteractiveServer</c>) - a transient Api outage should show an inline message, not take
    /// the page down.
    /// </remarks>
    private async Task LoadDataAsync(LoadDataArgs args)
    {
        isLoading = true;
        loadErrorMessage = null;

        int pageSize = args.Top ?? 10;
        int pageNumber = (args.Skip ?? 0) / pageSize + 1;
        string? sort = ToJsonApiSort(args.Sorts);

        try
        {
            (IReadOnlyList<EventDto> pageEvents, int total) =
                await EventClient.GetEventsAsync(pageNumber, pageSize, sort, CancellationToken.None);

            events = pageEvents;
            totalCount = total;
        }
        catch (EventDataUnavailableException)
        {
            events = [];
            totalCount = 0;
            loadErrorMessage = "Something went wrong loading Events. Try refreshing the page.";
        }

        isLoading = false;
    }

    /// <remarks>Maps Radzen's <see cref="SortDescriptor"/> onto JSON:API's <c>sort=</c>/<c>sort=-</c> syntax.</remarks>
    private static string? ToJsonApiSort(IEnumerable<SortDescriptor>? sorts)
    {
        SortDescriptor? first = sorts?.FirstOrDefault();
        if (first?.Property is not { } property)
        {
            return null;
        }

        property = property.ToLowerInvariant();
        return first.SortOrder == SortOrder.Descending ? $"-{property}" : property;
    }
}
