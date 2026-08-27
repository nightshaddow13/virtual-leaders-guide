using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using VirtualLeadersGuide.Web.Authorization;
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
    private string? statusMessage;
    private string? errorMessage;
    private bool isBusy;

    protected override async Task OnParametersSetAsync()
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

    private void NavigateToUsers() => NavigationManager.NavigateTo("dashboard/users", forceLoad: true);
}
