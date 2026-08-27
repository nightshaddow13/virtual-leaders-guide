using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Radzen;
using VirtualLeadersGuide.Web.Directors;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class InviteDirectorDialog
{
    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Inject]
    private DirectorInviteService InviteService { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    /// <remarks>
    /// The whole three-step flow (frame 3b) lives in one component, opened via
    /// <c>DialogService.OpenAsync&lt;InviteDirectorDialog&gt;</c> from <c>Users.razor</c> - simpler than
    /// wiring step transitions across separate dialog opens. <see cref="DialogService.Close(object?)"/>'s
    /// return value tells the caller whether to reload its grid: <see langword="true"/> only from
    /// <see cref="Step.Sent"/>'s Done button, since that's the only path that changed anything the grid
    /// would show.
    /// </remarks>
    private enum Step { EnterEmail, NewEmail, ExistingUser, Sent }

    private Step step = Step.EnterEmail;
    private readonly EmailModel emailModel = new();
    private string? displayName;
    private string? errorMessage;
    private bool isBusy;
    private UserRowDto? existingUser;

    private async Task ContinueAsync()
    {
        isBusy = true;
        errorMessage = null;

        InviteLookup lookup = await InviteService.LookUpAsync(emailModel.Email, CancellationToken.None);
        if (lookup.IsExistingUser)
        {
            existingUser = lookup.ExistingUser;
            step = Step.ExistingUser;
        }
        else
        {
            step = Step.NewEmail;
        }

        isBusy = false;
    }

    /// <remarks>
    /// <see cref="InviteOutcome.AlreadyOnPlatform"/> here (as opposed to from <see cref="ContinueAsync"/>'s
    /// own lookup) means a concurrent invite for the same email won the race between the two - re-run the
    /// lookup and fall through to the same existing-user step rather than erroring.
    /// </remarks>
    private async Task SendInvitationAsync()
    {
        isBusy = true;
        errorMessage = null;

        InviteOutcome outcome = await InviteService.InviteAsync(
            emailModel.Email, NullIfBlank(displayName), CancellationToken.None);

        switch (outcome)
        {
            case InviteOutcome.Invited:
                step = Step.Sent;
                break;
            case InviteOutcome.AlreadyOnPlatform:
                InviteLookup lookup = await InviteService.LookUpAsync(emailModel.Email, CancellationToken.None);
                existingUser = lookup.ExistingUser;
                step = Step.ExistingUser;
                break;
            default:
                errorMessage = "Something went wrong sending the invite. Try again.";
                break;
        }

        isBusy = false;
    }

    private void OpenExistingUser()
    {
        DialogService.Close(false);
        NavigationManager.NavigateTo($"dashboard/users/{existingUser!.Id}");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class EmailModel
    {
        [Required(ErrorMessage = "Enter an email.")]
        [EmailAddress(ErrorMessage = "Enter a valid email.")]
        public string Email { get; set; } = "";
    }
}
