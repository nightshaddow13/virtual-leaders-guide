using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;

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
    private NotificationService NotificationService { get; set; } = default!;

    [Parameter]
    public Guid EventId { get; set; }

    /// <remarks><see langword="null"/> on <c>.../activities/new</c>; set on <c>.../activities/{ActivityId:guid}</c> - same shape as <c>InfoPageEditor.InfoPageId</c>.</remarks>
    [Parameter]
    public Guid? ActivityId { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, Unavailable, Ready }

    private PageState state = PageState.Loading;
    private string? eventName;
    private bool isAdmin;
    private ActivityFormModel? model;
    private string? saveErrorMessage;
    private string? permissionLostMessage;
    private bool isSaving;

    /// <remarks>
    /// Gates on <see cref="ApiEventClient.GetEventAsync"/> first, exactly like <c>InfoPageEditor.razor.cs</c>.
    /// On <c>/new</c> that Event check is the entire access check, since the route carries only an
    /// <see cref="EventId"/>. When editing it isn't enough on its own - it doesn't confirm this specific
    /// Activity exists or belongs to this Event - so the Activity is read too, and one whose
    /// <see cref="ActivityDto.EventId"/> doesn't match the route's <see cref="EventId"/> (a stale or
    /// hand-edited URL) falls into <see cref="PageState.Denied"/> rather than a new state. The Denied copy
    /// still says "Event" for both routes: on <c>/new</c> the denial genuinely is about the Event, and when
    /// editing the Event gate fires first in the common case.
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

        if (ActivityId is null)
        {
            model = new ActivityFormModel();
            state = PageState.Ready;
            return;
        }

        ActivityReadOutcome activityOutcome;
        ActivityDto? activityDto;
        try
        {
            (activityOutcome, activityDto) = await ActivityClient.GetActivityAsync(ActivityId.Value, CancellationToken.None);
        }
        catch (ActivityDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        if (activityOutcome != ActivityReadOutcome.Success || activityDto is null || activityDto.EventId != EventId)
        {
            state = PageState.Denied;
            return;
        }

        model = new ActivityFormModel { Name = activityDto.Name, Description = activityDto.Description };
        state = PageState.Ready;
    }

    /// <remarks>
    /// Wired to <c>EditForm</c>'s <c>OnValidSubmit</c>, matching <c>InfoPageEditor.razor.cs</c>'s pattern -
    /// <see cref="ActivityFormModel.Name"/>'s <see cref="StringLengthAttribute"/> exists specifically to stop
    /// an over-length Name from ever reaching Api (which would 500, not 422, on this one field), so submission
    /// has to actually be gated on it, not just display a message beside an unblocked Save.
    /// </remarks>
    private async Task SaveAsync()
    {
        isSaving = true;
        saveErrorMessage = null;

        try
        {
            if (ActivityId is null)
            {
                await CreateAsync();
            }
            else
            {
                await UpdateAsync(ActivityId.Value);
            }
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

    /// <remarks>
    /// Lands on the new Activity's own edit page rather than the list, matching
    /// <c>InfoPageEditor.razor.cs</c> - and so that once the Placement builder arrives on that page (P5-11,
    /// #96), creating an Activity flows straight into placing it. The navigation is in-circuit (no
    /// <c>forceLoad</c>): this same component instance survives, <see cref="ActivityId"/> goes from
    /// <see langword="null"/> to set, and <see cref="OnParametersSetAsync"/> re-reads the saved Activity.
    /// <see cref="ActivityWriteOutcome.Invalid"/> is only reachable via the unknown-Event pointer, which
    /// normal navigation can't trigger since <see cref="EventId"/> always comes from an already-verified
    /// route - there is no form field to attach that error to.
    /// </remarks>
    private async Task CreateAsync()
    {
        (ActivityWriteOutcome outcome, ActivityDto? created, IReadOnlyList<string> pointers) = await ActivityClient.CreateAsync(
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
        NavigationManager.NavigateTo($"dashboard/events/{EventId}/activities/{created!.Id}");
    }

    /// <remarks>
    /// <see cref="ActivityWriteOutcome.Forbidden"/> here is claim-lag - a Director removed from the Event
    /// mid-session - not an out-of-scope navigation, matching <c>InfoPageEditor.razor.cs</c>'s
    /// <c>UpdateAsync</c>/<c>permissionLostMessage</c> distinction from <see cref="PageState.Denied"/>.
    /// </remarks>
    private async Task UpdateAsync(Guid id)
    {
        ActivityWriteOutcome outcome = await ActivityClient.UpdateAsync(
            id, model!.Name ?? string.Empty, model.Description ?? string.Empty, CancellationToken.None);

        if (outcome == ActivityWriteOutcome.Forbidden)
        {
            permissionLostMessage = "You no longer have permission to edit this Activity.";
            return;
        }

        NotificationService.Notify(NotificationSeverity.Success, "Changes saved");
        NavigateToList();
    }

    /// <remarks><c>forceLoad: true</c> - same reasoning as <c>InfoPageEditor.razor.cs</c>'s <c>NavigateToList</c>: crossing from this <c>prerender: false</c> page to another needs a real browser navigation, not an in-circuit one.</remarks>
    private void NavigateToList() => NavigationManager.NavigateTo($"dashboard/events/{EventId}/activities", forceLoad: true);

    private sealed class ActivityFormModel
    {
        [Required(ErrorMessage = "Enter a name.")]
        [StringLength(200, ErrorMessage = "Name can't be longer than 200 characters.")]
        public string? Name { get; set; }

        /// <remarks>
        /// No <see cref="RequiredAttribute"/> - empty content is legal at the Api layer (an Activity is real
        /// and listable before it has a Description, CONTEXT.md's Activity entry). Bound in
        /// <c>ActivityEditor.razor</c> through <c>MarkdownField</c>, whose <c>Value</c> remarks explain why the
        /// editing surface is a native <c>&lt;textarea&gt;</c> rather than <c>InputTextArea</c>.
        /// </remarks>
        public string? Description { get; set; }
    }
}
