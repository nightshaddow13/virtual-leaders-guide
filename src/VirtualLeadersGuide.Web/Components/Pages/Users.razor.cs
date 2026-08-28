using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using Radzen.Blazor;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Directors;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class Users
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiDirectorClient DirectorClient { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    /// <remarks>
    /// Same icon-only convention as <see cref="Pages.Dashboard"/>'s action column - see ADR-0037.
    /// </remarks>
    private const string ActionColumnWidth = "76px";

    /// <remarks>
    /// A real class, not a named tuple - <c>RadzenDropDown</c>'s <c>ValueProperty</c>/<c>TextProperty</c>
    /// resolve via reflection over actual properties, which a <see cref="ValueTuple{T1,T2}"/> doesn't expose
    /// under its element names (only <c>Item1</c>/<c>Item2</c> fields survive to runtime).
    /// </remarks>
    private sealed record StateFilterOption(string Key, string Text);

    private static readonly IReadOnlyList<StateFilterOption> StateFilterOptionsSource =
    [
        new("All", "All"),
        new("Active", "Active"),
        new("Invited", "Invited")
    ];

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Ready }

    private PageState state = PageState.Loading;
    private RadzenDataGrid<UserRowDto>? grid;
    private IEnumerable<UserRowDto>? users;
    private int totalCount;
    private bool isLoading;
    private string? loadErrorMessage;
    private string searchText = "";
    private string stateFilterKey = "All";

    private IEnumerable<StateFilterOption> stateFilterOptions => StateFilterOptionsSource;

    /// <remarks>
    /// Admin-only, unlike <see cref="Pages.Dashboard"/>'s any-role check - a Director has no business on
    /// this screen at all, so a non-Admin renders an inline <see cref="PageState.Denied"/> panel rather than
    /// <c>NavigationManager.NavigateTo("Account/NoAccess")</c>, matching <see cref="Pages.EventEditor"/>'s
    /// pattern (this page shares its <c>prerender: false</c> reasoning - ADR-0036).
    /// </remarks>
    protected override async Task OnInitializedAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState authState = await AuthenticationStateTask;
        var accessView = new EventAccessView(authState.User);

        state = accessView.IsAdmin ? PageState.Ready : PageState.Denied;
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        isLoading = true;
        loadErrorMessage = null;

        int pageSize = args.Top ?? 10;
        int pageNumber = (args.Skip ?? 0) / pageSize + 1;
        UserState? stateFilter = stateFilterKey switch
        {
            "Active" => VirtualLeadersGuide.Web.Directors.UserState.Active,
            "Invited" => VirtualLeadersGuide.Web.Directors.UserState.Invited,
            _ => null
        };

        try
        {
            (IReadOnlyList<UserRowDto> pageUsers, int total) = await DirectorClient.GetUsersAsync(
                pageNumber, pageSize, searchText, stateFilter, CancellationToken.None);

            users = pageUsers;
            totalCount = total;
        }
        catch (DirectorDataUnavailableException)
        {
            users = [];
            totalCount = 0;
            loadErrorMessage = "Something went wrong loading Users. Try refreshing the page.";
        }

        isLoading = false;
    }

    private static string StateLabel(UserRowDto row) => row.HasCredential
        ? row.EventGrantCount > 0 ? $"Active · {row.EventGrantCount} events" : "Active · 0 events"
        : "Invited";

    private async Task OpenInviteDialogAsync()
    {
        await DialogService.OpenAsync<InviteDirectorDialog>("Invite a director",
            options: new DialogOptions { Width = "480px", CloseDialogOnOverlayClick = true });

        await grid!.Reload();
    }

    private void NavigateToDashboard() => NavigationManager.NavigateTo("dashboard", forceLoad: true);
}
