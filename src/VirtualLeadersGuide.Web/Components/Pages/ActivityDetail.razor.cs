using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Radzen;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>
/// The one page for an existing Activity (P5-23, #206; wireframe turn 2, Direction C): Name, Description and
/// the Placement builder beside the live "Where it appears" tree. "Place it" adds an unsaved ghost row;
/// <see cref="SaveAsync"/> commits the Name/Description and the ghosts from one button; leaving with either
/// pending asks once first. Removing a saved row (P5-12, #97) writes immediately - see <see cref="RemoveAsync"/>.
/// </summary>
/// <remarks>
/// One Save is not one transaction: its two parts succeed or fail on their own (ADR-0072's P5-23 amendment).
/// Everything that displays the Activity's name - tree rows, ghosts, dialogs, breadcrumb, title - shows the
/// saved <see cref="activity"/>, never what is being typed into <see cref="form"/>.
/// <para>
/// Leaving is guarded twice: in-app navigation by <see cref="ConfirmLeavingAsync"/>, and reload, tab close and
/// external links by a <c>NavigationLock</c>, which can only raise the browser's own prompt.
/// </para>
/// <para>
/// In the markup, the <c>fieldset</c> exists only so that losing permission disables the details and the builder
/// together. The <c>EditForm</c> stops short of the builder: with it inside, Enter in one of its autofill fields
/// would submit.
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

    [Parameter]
    public Guid EventId { get; set; }

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
    private PlacementTreeModel? model;
    private readonly ActivityFormModel form = new();
    private EditContext editContext = default!;
    private string? saveErrorMessage;
    private bool permissionLost;
    private bool isSaving;
    private IDisposable? locationChangingRegistration;

    private const string PermissionLostText = "You no longer have permission to edit this Activity.";
    private const string RefreshFailedText = "Couldn't refresh the placements, so the tree may be out of date. Reload the page.";

    private bool WriteBlocked => isSaving || isRemoving || permissionLost;

    private int PlacedCount => tree.Placements.Count(p => p.ActivityId == ActivityId) + ghosts.Count;

    private bool IsDirty => activity is not null
        && ((form.Name ?? string.Empty) != activity.Name || (form.Description ?? string.Empty) != activity.Description);

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

    /// <remarks>
    /// Registered after first render, not in initialization: a location-changing handler only fires inside
    /// an interactive circuit, and this page is <c>prerender: false</c>, so the first render already is one.
    /// </remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            locationChangingRegistration = NavigationManager.RegisterLocationChangingHandler(ConfirmLeavingAsync);
        }
    }

    /// <summary>Prompts once when leaving with Name/Description edits or ghost rows pending; Cancel goes through the same guard, which is how "Cancel discards them all" is confirmed.</summary>
    private async ValueTask ConfirmLeavingAsync(LocationChangingContext context)
    {
        if (!HasUnsavedChanges)
        {
            return;
        }

        var unsaved = new List<string>();
        if (IsDirty)
        {
            unsaved.Add("Name/Description changes");
        }

        unsaved.AddRange(ghosts.Select(g => g.Display));
        if (isSaving)
        {
            unsaved.Add("A save is still running, so some of these may already be saved");
        }

        var parameters = new Dictionary<string, object?>
        {
            [nameof(ConfirmDialog.Message)] = "You have unsaved changes.",
            [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>)[.. unsaved, "Leaving discards them"],
            [nameof(ConfirmDialog.ConfirmText)] = "Discard and leave",
            [nameof(ConfirmDialog.DismissText)] = "Keep editing"
        };

        bool? leave = await DialogService.OpenAsync<ConfirmDialog>("Discard unsaved changes?", parameters);
        if (leave is not true)
        {
            context.PreventNavigation();
        }
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

    /// <summary>The header button's route into <see cref="SaveAsync"/>, which sits outside the form and so validates it by hand. An invalid Name blocks the whole Save, ghosts included.</summary>
    private async Task SubmitAsync()
    {
        if (await editContext.ValidateAsync())
        {
            await SaveAsync();
        }
    }

    /// <remarks>
    /// Two independent parts: the Name/Description PATCH (only if edited), then the ghosts. A failed PATCH
    /// doesn't hold the Placements back, and vice versa; whatever failed stays pending so the next Save retries
    /// only that. A 403 is the exception - the same access covers both, so nothing further is sent, and the tree
    /// isn't reloaded either: without access Api answers those reads with empty lists, which would blank it.
    /// </remarks>
    private async Task SaveAsync()
    {
        if (WriteBlocked || !HasUnsavedChanges)
        {
            return;
        }

        isSaving = true;
        saveErrorMessage = null;
        var problems = new List<string>();

        try
        {
            bool detailsSaved = IsDirty && await SaveDetailsAsync(problems);
            bool placementsSaved = !permissionLost && ghosts.Count > 0 && await SaveGhostsAsync(problems);

            if (permissionLost)
            {
                RebuildModel();
            }
            else if (!await TryRefreshTreeAsync())
            {
                problems.Add(RefreshFailedText);
            }

            saveErrorMessage = problems.Count > 0 ? string.Join(" ", problems) : null;

            string? saved = (detailsSaved, placementsSaved) switch
            {
                (true, true) => "Changes and placements saved",
                (true, false) => "Changes saved",
                (false, true) => "Placements saved",
                _ => null
            };
            if (saved is not null)
            {
                NotificationService.Notify(NotificationSeverity.Success, saved);
            }
        }
        finally
        {
            isSaving = false;
        }
    }

    /// <summary>PATCHes the Name and Description, adopting them as the saved <see cref="activity"/> on success.</summary>
    /// <param name="problems">Gets a line to show the user if the write failed.</param>
    /// <returns>Whether they were saved.</returns>
    private async Task<bool> SaveDetailsAsync(List<string> problems)
    {
        string name = form.Name ?? string.Empty;
        string description = form.Description ?? string.Empty;

        try
        {
            if (await ActivityClient.UpdateAsync(ActivityId, name, description, CancellationToken.None) == ActivityWriteOutcome.Forbidden)
            {
                permissionLost = true;
                return false;
            }
        }
        catch (ActivityDataUnavailableException)
        {
            problems.Add("Couldn't save the Name and Description. Try again.");
            return false;
        }

        activity = new ActivityDto { Id = ActivityId, EventId = EventId, Name = name, Description = description };
        return true;
    }

    /// <remarks>
    /// Sends each ghost as its own request, in the order they were placed - not one batch - so a ghost that
    /// introduces a Tab is already persisted when a later ghost reuses it by name (Api resolves each by name,
    /// ADR-0072). Stops at the first rejection and keeps that ghost and every later one pending, so nothing
    /// the user placed is lost to a partial failure; a 409 (already placed - the stale-tree race) is dropped as
    /// satisfied rather than treated as a failure. When only some got saved, the saved ones are named too.
    /// </remarks>
    /// <returns>Whether every ghost was newly saved - <see langword="false"/> if any was rejected or skipped.</returns>
    private async Task<bool> SaveGhostsAsync(List<string> problems)
    {
        var saved = new List<string>();
        var alreadyPlaced = new List<string>();
        bool stopped = false;

        try
        {
            foreach (PlacementPath ghost in ghosts.ToList())
            {
                (PlacementWriteOutcome outcome, _, _) = await PlacementClient.CreateAsync(
                    ActivityId, ghost.Tab, ghost.SubTab, ghost.Section, ghost.SubSection, CancellationToken.None);

                if (outcome == PlacementWriteOutcome.Forbidden)
                {
                    permissionLost = true;
                    stopped = true;
                    break;
                }

                if (outcome == PlacementWriteOutcome.Invalid)
                {
                    problems.Add($"Couldn't save \"{ghost.Display}\". Check its levels and try again.");
                    stopped = true;
                    break;
                }

                ghosts.Remove(ghost);
                (outcome == PlacementWriteOutcome.Conflict ? alreadyPlaced : saved).Add(ghost.Display);
            }
        }
        catch (ActivityDataUnavailableException)
        {
            problems.Add("Something went wrong saving these placements. Try again.");
            stopped = true;
        }

        if (alreadyPlaced.Count > 0)
        {
            problems.Add($"Already placed, so skipped: {string.Join(", ", alreadyPlaced)}.");
        }

        bool allSaved = !stopped && alreadyPlaced.Count == 0;
        if (!allSaved && saved.Count > 0)
        {
            problems.Add($"Saved: {string.Join(", ", saved)}.");
        }

        return allSaved;
    }

    /// <summary>Puts the saved Name and Description back into the form, leaving it clean.</summary>
    private void ResetForm()
    {
        form.Name = activity!.Name;
        form.Description = activity.Description;
    }

    private async Task LoadTreeAsync()
    {
        tree = await PlacementClient.GetTreeForEventAsync(EventId, CancellationToken.None);
        (IReadOnlyList<ActivityDto> activities, _) =
            await ActivityClient.GetActivitiesForEventAsync(EventId, 1, 9999, null, CancellationToken.None);
        activityNames = activities.ToDictionary(a => a.Id, a => a.Name);
    }

    private void RebuildModel() =>
        model = new PlacementTreeModel(tree, ActivityId, activity!.Name, activityNames, ghosts);

    private void NavigateToList() => NavigationManager.NavigateTo($"dashboard/events/{EventId}/activities");

    /// <inheritdoc/>
    public void Dispose() => locationChangingRegistration?.Dispose();
}
