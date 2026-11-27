using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Radzen;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>
/// The one page for an existing Activity (P5-23, #206; wireframe turn 2, Direction C): Name, Description and
/// beside the live "Where it appears" tree with the Placement builder under it. "Place it" adds an unsaved ghost row;
/// <see cref="SaveAsync"/> commits the Name/Description and the ghosts from one button; leaving with either
/// pending asks once first. Removing a saved row (P5-12, #97) writes immediately - see <see cref="RemoveAsync"/>.
/// </summary>
/// <remarks>
/// Loading and page state live here, saving and the leave guard in <c>ActivityDetail.Save.cs</c>, removal in
/// <c>ActivityDetail.Removal.cs</c>. Everything that displays the Activity's name - tree rows, ghosts, dialogs,
/// breadcrumb, title - shows the saved <see cref="activity"/>, never what is being typed into <see cref="form"/>.
/// <para>
/// The builder sits under the tree rather than under the Description where the wireframe draws it (asked for
/// after seeing the page), and the tree is left out while it would be empty.
/// </para>
/// <para>
/// In the markup, the two <c>fieldset</c>s exist only so that losing permission disables the details and the
/// builder. The builder is outside the <c>EditForm</c>: inside it, Enter in an autofill field would submit.
/// </para>
/// </remarks>
public partial class ActivityDetail : IDisposable
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiEventClient EventClient { get; set; } = default!;

    [Inject]
    private ApiActivityClient ActivityClient { get; set; } = default!;

    [Inject]
    private ApiPlacementClient PlacementClient { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    /// <summary>The Event from the route.</summary>
    [Parameter]
    public Guid EventId { get; set; }

    /// <summary>The Activity being edited, from the route.</summary>
    [Parameter]
    public Guid ActivityId { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    private enum PageState { Loading, Denied, NotFound, Unavailable, Ready }

    private PageState state = PageState.Loading;
    private string? eventName;
    private bool isAdmin;
    private ActivityDto? activity;
    private PlacementTreeDto tree = PlacementTreeDto.Empty;
    private Dictionary<Guid, string> activityNames = [];
    private readonly List<PlacementPath> ghosts = [];

    /// <summary>Placements written since <see cref="tree"/> was last read, so they stay drawn when it can't be read again (access lost, or the read failed).</summary>
    private readonly List<(Guid Id, PlacementPath Path)> savedSinceLoad = [];

    private PlacementTreeModel? model;
    private readonly ActivityFormModel form = new();
    private EditContext editContext = default!;
    private string? saveErrorMessage;
    private bool permissionLost;

    private const string PermissionLostText = "You no longer have permission to edit this Activity.";
    private const string RefreshFailedText = "Couldn't refresh the placements, so the tree may be out of date. Reload the page.";

    private bool WriteBlocked => isSaving || isRemoving || permissionLost;

    private int PlacedCount => tree.Placements.Count(p => p.ActivityId == ActivityId) + savedSinceLoad.Count + ghosts.Count;

    private bool IsDirty => activity is not null && form.Differs(activity.Name, activity.Description);

    private bool HasUnsavedChanges => IsDirty || ghosts.Count > 0;

    /// <inheritdoc/>
    protected override void OnInitialized() => editContext = new EditContext(form);

    /// <remarks>
    /// Gates on <see cref="ApiEventClient.GetEventAsync"/> first, exactly like <c>ActivityList.razor.cs</c> -
    /// this route carries an <see cref="EventId"/> that Api's own 403 (ADR-0031) vouches for - then reads the
    /// Activity and requires it to belong to that Event, so a hand-edited URL can't pair one Event's access
    /// with another Event's Activity.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (AuthenticationStateTask is null)
        {
            return;
        }

        AuthenticationState authState = await AuthenticationStateTask;
        if (!authState.User.IsStaff())
        {
            NavigationManager.NavigateTo("Account/NoAccess");
            return;
        }

        isAdmin = new EventAccessView(authState.User).IsAdmin;

        try
        {
            (EventReadOutcome eventOutcome, EventDto? eventDto) = await EventClient.GetEventAsync(EventId, CancellationToken.None);
            if (eventOutcome != EventReadOutcome.Success || eventDto is null)
            {
                state = PageState.Denied;
                return;
            }

            eventName = eventDto.Name;

            (ActivityReadOutcome activityOutcome, ActivityDto? activityDto) =
                await ActivityClient.GetActivityAsync(ActivityId, CancellationToken.None);
            if (activityOutcome == ActivityReadOutcome.Forbidden)
            {
                state = PageState.Denied;
                return;
            }

            if (activityOutcome == ActivityReadOutcome.NotFound || activityDto is null || activityDto.EventId != EventId)
            {
                state = PageState.NotFound;
                return;
            }

            activity = activityDto;
            ResetForm();
            await LoadTreeAsync();
        }
        catch (Exception ex) when (ex is EventDataUnavailableException or ActivityDataUnavailableException)
        {
            state = PageState.Unavailable;
            return;
        }

        RebuildModel();
        state = PageState.Ready;
    }

    /// <remarks>Empty on purpose: receiving the callback is what re-renders the page, so Save and the leave guard see the edit.</remarks>
    private void OnDetailsChanged()
    {
    }

    private Task OnPlace(PlacementPath path)
    {
        saveErrorMessage = null;
        ghosts.Add(path);
        RebuildModel();
        return Task.CompletedTask;
    }

    /// <summary>Puts the saved Name and Description back into the form, leaving it clean.</summary>
    private void ResetForm() => form.Load(activity!.Name, activity.Description);

    private async Task LoadTreeAsync()
    {
        tree = await PlacementClient.GetTreeForEventAsync(EventId, CancellationToken.None);
        savedSinceLoad.Clear();
        (IReadOnlyList<ActivityDto> activities, _) =
            await ActivityClient.GetActivitiesForEventAsync(EventId, 1, 9999, null, CancellationToken.None);
        activityNames = activities.ToDictionary(a => a.Id, a => a.Name);
    }

    private void RebuildModel() =>
        model = new PlacementTreeModel(tree, ActivityId, activity!.Name, activityNames, ghosts, savedSinceLoad);

    private void NavigateToList() => NavigationManager.NavigateTo($"dashboard/events/{EventId}/activities");
}
