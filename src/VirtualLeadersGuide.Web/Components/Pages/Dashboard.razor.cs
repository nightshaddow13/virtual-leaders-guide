using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using Radzen.Blazor;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Directors;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.Time;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class Dashboard
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    [Inject]
    private ApiDirectorClient DirectorClient { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Inject]
    private BrowserTimeZoneAccessor TimeZoneAccessor { get; set; } = default!;

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
    /// Not the icon buttons' own ~44px each: the grid renders with a fixed table layout
    /// (<c>rz-grid-table-fixed</c>), so a column can never grow to fit its content - one narrower than the
    /// buttons clips them against the cell's padding box instead of just looking cramped. Two buttons (Edit/
    /// View plus, for an Admin, Delete - P2-17, #112) at ~44px each, a 4px gap between them, and the same
    /// breathing room on both sides that 76px reserved for one button, comes to 128px. The buttons themselves
    /// are icon-only, not text - see ADR-0037 for the row-action convention this establishes.
    /// </remarks>
    private const string ActionColumnWidth = "128px";

    /// <remarks>
    /// Same fixed-table-layout reasoning as <see cref="ActionColumnWidth"/> - the STATUS column's own badge
    /// plus the built-in filter row's <c>RadzenDropDown</c> both need room, or either clips against the
    /// cell's padding box once the grid stops letting columns grow to fit content.
    /// </remarks>
    private const string StatusColumnWidth = "160px";

    private RadzenDataGrid<EventDto>? grid;
    private EventAccessView? accessView;
    private EventStatusFilter statusFilter = EventStatusFilter.Current;

    /// <remarks>
    /// A real record, not a tuple - <c>RadzenDropDown</c> resolves <c>TextProperty</c>/<c>ValueProperty</c> by
    /// reflection over property names, which a tuple's <c>Item1</c>/<c>Item2</c> can't satisfy (same reasoning
    /// as <c>Users.razor.cs</c>'s own status filter). <see cref="EventStatusFilter.Current"/> reads "Current",
    /// not "All" - it's Draft plus not-yet-elapsed Live, and a default reading "All" would be false the moment
    /// a Past or Cancelled Event exists.
    /// </remarks>
    private static readonly IReadOnlyList<StatusFilterOption> statusFilterOptions =
    [
        new(EventStatusFilter.Current, "Current"),
        new(EventStatusFilter.All, "All"),
        new(EventStatusFilter.Draft, "Draft"),
        new(EventStatusFilter.Live, "Live"),
        new(EventStatusFilter.Past, "Past"),
        new(EventStatusFilter.Cancelled, "Cancelled")
    ];

    private sealed record StatusFilterOption(EventStatusFilter Key, string Text);

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
    private string? deleteErrorMessage;

    /// <remarks>
    /// Starts UTC and is replaced once <see cref="OnAfterRenderAsync"/> resolves the real one - grid rows
    /// only ever exist after <see cref="LoadDataAsync"/>, itself a circuit round trip that can't complete
    /// before first render, so there's no frame where a DATES cell renders in the wrong zone (see
    /// <see cref="BrowserTimeZoneAccessor"/>'s remarks and ADR-0043).
    /// </remarks>
    private TimeZoneInfo viewerZone = TimeZoneInfo.Utc;

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
    /// Interop is only legal once the circuit has connected - <paramref name="firstRender"/> is the earliest
    /// safe point. Re-renders the grid afterward so any already-loaded rows pick up the resolved zone
    /// instead of staying on the UTC fallback for the rest of the page's life.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        viewerZone = await TimeZoneAccessor.GetTimeZoneAsync();
        StateHasChanged();
    }

    private string FormatDates(EventDto item) =>
        EventDateRange.Format(item.StartsAt, item.EndsAt, viewerZone, DateTimeOffset.UtcNow);

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
                await EventClient.GetEventsAsync(pageNumber, pageSize, sort, statusFilter, CancellationToken.None);

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

    /// <remarks>
    /// Wrapped in try/catch for the same reason <see cref="LoadDataAsync"/> is - an uncaught exception out of
    /// a Radzen callback crashes the whole circuit. The Director count feeding the confirm dialog's
    /// consequence list is its own fetch (<see cref="ApiDirectorClient.GetDirectorCountForEventAsync"/>) that
    /// can fail independently of the delete itself; per ADR-0045, a failure there degrades that one bullet
    /// rather than blocking the dialog from opening at all - an Admin's ability to delete a broken Event
    /// shouldn't depend on a transient failure in data that's only advisory to begin with. A 404 from the
    /// delete call itself is treated as silent success (grid reloads, no message) since the Admin's intent -
    /// "this Event shouldn't exist" - is already satisfied by the row already being gone.
    /// </remarks>
    private async Task DeleteAsync(EventDto item)
    {
        deleteErrorMessage = null;

        try
        {
            int? directorCount;
            try
            {
                directorCount = await DirectorClient.GetDirectorCountForEventAsync(item.Id, CancellationToken.None);
            }
            catch (DirectorDataUnavailableException)
            {
                directorCount = null;
            }

            var parameters = EventDeleteConfirmation.BuildDialogParameters(item.Name, item.Slug, directorCount);

            bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>("Delete event?", parameters);
            if (confirmed is not true)
            {
                return;
            }

            EventWriteOutcome outcome = await EventClient.DeleteAsync(item.Id, CancellationToken.None);
            if (outcome is EventWriteOutcome.Success or EventWriteOutcome.NotFound)
            {
                await grid!.Reload();
            }
            else if (outcome == EventWriteOutcome.Forbidden)
            {
                deleteErrorMessage = "You don't have permission to delete this Event.";
            }
        }
        catch (EventDataUnavailableException)
        {
            deleteErrorMessage = "Something went wrong deleting this Event. Try again.";
        }
    }

    /// <remarks>
    /// Maps Radzen's <see cref="SortDescriptor"/> onto JSON:API's <c>sort=</c>/<c>sort=-</c> syntax.
    /// Lowercases only the first character, not the whole property name - JsonApiDotNetCore's default
    /// naming exposes an attribute in camelCase (<c>startsAt</c>, not <c>startsat</c>); lowering the whole
    /// string was harmless while every sortable column's name was one word (<c>name</c>, <c>slug</c>) but
    /// would 400 a sort on <see cref="EventDto.StartsAt"/>.
    /// </remarks>
    private static string? ToJsonApiSort(IEnumerable<SortDescriptor>? sorts)
    {
        SortDescriptor? first = sorts?.FirstOrDefault();
        if (first?.Property is not { Length: > 0 } property)
        {
            return null;
        }

        property = char.ToLowerInvariant(property[0]) + property[1..];
        return first.SortOrder == SortOrder.Descending ? $"-{property}" : property;
    }
}
