using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Directors;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class UserDetail
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiDirectorClient DirectorClient { get; set; } = default!;

    [Inject]
    private DirectorInviteService InviteService { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Inject]
    private TooltipService TooltipService { get; set; } = default!;

    /// <remarks>
    /// "LINK EXPIRES" (frame 3c) is deliberately omitted - it needs an invited-at timestamp this ticket
    /// doesn't store (deferred to #101); every other field on the wireframe's card is derivable today.
    /// </remarks>
    [Parameter]
    public string Id { get; set; } = "";

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Ready }

    private PageState state = PageState.Loading;
    private UserRowDto? user;
    private string callerUserId = "";
    private string? statusMessage;
    private string? errorMessage;
    private bool isBusy;
    private bool isDeleting;
    private ElementReference deleteGuardAnchor;

    /// <remarks>
    /// The signed-in Admin's own detail page, and a User holding Admin, are both guarded (ADR-0045) - only
    /// checked once <see cref="user"/> is loaded, so this reads <see langword="false"/> (button disabled,
    /// same rendering as a guarded target) rather than throwing during the brief window before that fetch
    /// completes.
    /// </remarks>
    private bool CanDelete => user is { HasCredential: true, IsAdmin: false } && user.Id != callerUserId;

    private string DeleteGuardTooltipText => user!.Id == callerUserId
        ? "This is your own account. Delete it from Account settings > Personal data."
        : "Admins can't be deleted here. Remove their email from the admin allowlist instead.";

    protected override async Task OnParametersSetAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState authState = await AuthenticationStateTask;
        callerUserId = authState.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        if (!new EventAccessView(authState.User).IsAdmin)
        {
            state = PageState.Denied;
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            user = await DirectorClient.GetUserAsync(Id, CancellationToken.None);
        }
        catch (DirectorDataUnavailableException)
        {
            user = null;
        }

        state = user is null ? PageState.Denied : PageState.Ready;
    }

    private async Task ResendAsync()
    {
        isBusy = true;
        statusMessage = null;
        errorMessage = null;

        ResendOutcome outcome = await InviteService.ResendAsync(Id, CancellationToken.None);
        statusMessage = outcome switch
        {
            ResendOutcome.Sent => "Invite resent.",
            _ => null
        };
        if (outcome != ResendOutcome.Sent)
        {
            errorMessage = "Couldn't resend the invite - refresh and try again.";
        }

        isBusy = false;
    }

    private async Task RevokeAsync()
    {
        isBusy = true;

        RevokeOutcome outcome = await InviteService.RevokeAsync(Id, CancellationToken.None);
        if (outcome == RevokeOutcome.Revoked)
        {
            NotificationService.Notify(NotificationSeverity.Success, "Invite revoked");
            NavigateToUsers();
            return;
        }

        errorMessage = "Couldn't revoke the invite - refresh and try again.";
        isBusy = false;
    }

    /// <remarks>
    /// Mirrors <c>EventEditor.razor.cs.DeleteAsync</c>'s dialog-then-write shape. <see cref="UserDeleteOutcome.Deleted"/>
    /// and <see cref="UserDeleteOutcome.NotFound"/> both notify and navigate away - a stale target reads as
    /// success, matching <c>EventWriteOutcome.NotFound</c>'s own reasoning. <see cref="UserDeleteOutcome.TargetIsAdmin"/>/
    /// <see cref="UserDeleteOutcome.TargetIsSelf"/> are normally unreachable, since <see cref="CanDelete"/>
    /// already disables the button for both - covered here only for a page gone stale between render and
    /// click.
    /// </remarks>
    private async Task DeleteAsync()
    {
        errorMessage = null;

        string label = user!.DisplayName ?? user.Email;
        Dictionary<string, object?> parameters =
            UserDeleteConfirmation.BuildDialogParameters(label, user.EventGrantCount);

        bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>("Delete user?", parameters);
        if (confirmed is not true)
        {
            return;
        }

        isDeleting = true;

        UserDeleteOutcome outcome = await InviteService.DeleteAsync(Id, callerUserId, CancellationToken.None);
        if (outcome is UserDeleteOutcome.Deleted or UserDeleteOutcome.NotFound)
        {
            NotificationService.Notify(NotificationSeverity.Success, "User deleted");
            NavigateToUsers();
            return;
        }

        errorMessage = "Couldn't delete this User - refresh and try again.";
        isDeleting = false;
    }

    private void NavigateToUsers() => NavigationManager.NavigateTo("dashboard/users", forceLoad: true);
}
