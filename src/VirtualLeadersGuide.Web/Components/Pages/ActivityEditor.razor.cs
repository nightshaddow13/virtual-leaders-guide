using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Components.Pages;

public partial class ActivityEditor
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    [Inject]
    private ApiActivityClient ActivityClient { get; set; } = default!;

    [Inject]
    private MarkdownRenderer MarkdownRenderer { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Parameter]
    public Guid EventId { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Ready }

    /// <remarks>Drives the responsive Write/Preview pane switch, same grilled decision as <c>InfoPageEditor.razor.cs</c> - see <c>ActivityEditor.razor.css</c>. Only meaningful below the 64rem breakpoint; ignored above it, where both panes always show.</remarks>
    private enum PaneView { Write, Preview }

    private PageState state = PageState.Loading;
    private PaneView activePane = PaneView.Write;
    private string? eventName;
    private bool isAdmin;
    private ActivityFormModel? model;
    private string? saveErrorMessage;
    private bool isSaving;

    /// <remarks>
    /// Gates on <see cref="ApiEventClient.GetEventAsync"/> first, exactly like <c>InfoPageEditor.razor.cs</c>
    /// - this route carries only an <see cref="EventId"/>, so confirming the caller can read/write that Event
    /// is the entire access check this create-only form needs.
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

        EventReadOutcome eventOutcome;
        EventDto? eventDto;
        try
        {
            (eventOutcome, eventDto) = await EventClient.GetEventAsync(EventId, CancellationToken.None);
        }
        catch (EventDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        if (eventOutcome != EventReadOutcome.Success || eventDto is null)
        {
            state = PageState.Denied;
            return;
        }

        eventName = eventDto.Name;
        model = new ActivityFormModel();
        state = PageState.Ready;
    }

    /// <remarks>
    /// Wired to <c>EditForm</c>'s <c>OnValidSubmit</c>, matching <c>InfoPageEditor.razor.cs</c>'s pattern -
    /// <see cref="ActivityFormModel.Name"/>'s <see cref="StringLengthAttribute"/> exists specifically to stop
    /// an over-length Name from ever reaching Api (which would 500, not 422, on this one field), so submission
    /// has to actually be gated on it, not just display a message beside an unblocked Save.
    /// <see cref="ActivityWriteOutcome.Invalid"/> is only reachable via the unknown-Event pointer, which
    /// normal navigation can't trigger since <see cref="EventId"/> always comes from an already-verified
    /// route - there is no form field to attach that error to.
    /// </remarks>
    private async Task SaveAsync()
    {
        isSaving = true;
        saveErrorMessage = null;

        try
        {
            (ActivityWriteOutcome outcome, _, IReadOnlyList<string> pointers) = await ActivityClient.CreateAsync(
                EventId, model!.Name ?? string.Empty, model.Description ?? string.Empty, CancellationToken.None);

            if (outcome == ActivityWriteOutcome.Forbidden)
            {
                state = PageState.Denied;
                return;
            }

            if (outcome == ActivityWriteOutcome.Invalid)
            {
                saveErrorMessage = pointers.Count > 0
                    ? "This Event no longer exists. Try again from the dashboard."
                    : "Something went wrong saving this Activity. Try again.";
                return;
            }

            NotificationService.Notify(NotificationSeverity.Success, "Activity created");
            NavigationManager.NavigateTo($"dashboard/events/{EventId}", forceLoad: true);
        }
        catch (ActivityDataUnavailableException)
        {
            saveErrorMessage = "Something went wrong saving this Activity. Try again.";
        }
        finally
        {
            isSaving = false;
        }
    }

    private sealed class ActivityFormModel
    {
        [Required(ErrorMessage = "Enter a name.")]
        [StringLength(200, ErrorMessage = "Name can't be longer than 200 characters.")]
        public string? Name { get; set; }

        /// <remarks>
        /// No <see cref="RequiredAttribute"/> - empty content is legal at the Api layer (an Activity is real
        /// and listable before it has a Description, CONTEXT.md's Activity entry). Bound in
        /// <c>ActivityEditor.razor</c> to a plain native <c>&lt;textarea @bind-value:event="oninput"&gt;</c>,
        /// deliberately not the <c>InputTextArea</c> component - see <c>InfoPageEditor.razor.cs</c>'s
        /// <c>MarkdownContent</c> remarks for why <c>InputTextArea</c> can't be made to fire on input.
        /// </remarks>
        public string? Description { get; set; }
    }
}
