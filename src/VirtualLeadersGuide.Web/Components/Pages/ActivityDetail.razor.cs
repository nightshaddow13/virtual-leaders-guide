using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Radzen;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>
/// An Activity's page (P5-11, #96): the Placement builder beside the live "Where it appears" tree
/// (wireframe turn 2, Direction C). "Place it" adds an unsaved ghost row; <see cref="SaveAsync"/> is what
/// makes ghosts permanent; leaving with ghosts pending asks once first. Removing a saved row (P5-12, #97)
/// writes immediately - see <see cref="RemoveAsync"/>.
/// </summary>
/// <remarks>
/// Editing the Activity's Name and Description lives on this same page once P5-8 (#94) ships - this story
/// builds the page as the host for the Placement surface that ticket's wireframe assumes, because P5-8 had
/// not shipped an edit page to attach it to.
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
    private string? saveErrorMessage;
    private bool isSaving;
    private bool isRemoving;

    /// <summary>How long the "Removed from …" toast stays up for Undo, in milliseconds - longer than the 3 s default so the button can be reached.</summary>
    private const int UndoToastDuration = 8000;
    private IDisposable? locationChangingRegistration;

    private int PlacedCount => tree.Placements.Count(p => p.ActivityId == ActivityId) + ghosts.Count;

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
        if (!authState.User.Claims.Any(c => c.Type == ClaimTypes.Role))
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

    /// <summary>Prompts once when leaving with ghost rows pending (the ticket's "leaving prompts once"; Cancel goes through the same guard, which is how "Cancel discards them all" is confirmed).</summary>
    private async ValueTask ConfirmLeavingAsync(LocationChangingContext context)
    {
        if (ghosts.Count == 0 || isSaving)
        {
            return;
        }

        var parameters = new Dictionary<string, object?>
        {
            [nameof(ConfirmDialog.Message)] =
                ghosts.Count == 1 ? "You have 1 unsaved placement." : $"You have {ghosts.Count} unsaved placements.",
            [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>)[.. ghosts.Select(g => g.Display), "Leaving discards them"],
            [nameof(ConfirmDialog.ConfirmText)] = "Discard and leave",
            [nameof(ConfirmDialog.DismissText)] = "Keep editing"
        };

        bool? leave = await DialogService.OpenAsync<ConfirmDialog>("Discard unsaved placements?", parameters);
        if (leave is not true)
        {
            context.PreventNavigation();
        }
    }

    private Task OnPlace(PlacementPath path)
    {
        saveErrorMessage = null;
        ghosts.Add(path);
        RebuildModel();
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Sends each ghost as its own request, in the order they were placed - not one batch - so a ghost that
    /// introduces a Tab is already persisted when a later ghost reuses it by name (Api resolves each by name,
    /// ADR-0072). Stops at the first rejection and keeps that ghost and every later one pending, so nothing
    /// the user placed is lost to a partial failure; a 409 (already placed - the stale-tree race) is dropped as
    /// satisfied rather than treated as a failure. The tree is reloaded either way, so what's drawn matches
    /// what actually saved.
    /// </remarks>
    private async Task SaveAsync()
    {
        isSaving = true;
        saveErrorMessage = null;
        var alreadyPlaced = new List<string>();

        try
        {
            foreach (PlacementPath ghost in ghosts.ToList())
            {
                (PlacementWriteOutcome outcome, _, _) = await PlacementClient.CreateAsync(
                    ActivityId, ghost.Tab, ghost.SubTab, ghost.Section, ghost.SubSection, CancellationToken.None);

                if (outcome == PlacementWriteOutcome.Forbidden)
                {
                    state = PageState.Denied;
                    return;
                }

                if (outcome == PlacementWriteOutcome.Invalid)
                {
                    saveErrorMessage = $"Couldn't save \"{ghost.Display}\". Check its levels and try again.";
                    break;
                }

                ghosts.Remove(ghost);
                if (outcome == PlacementWriteOutcome.Conflict)
                {
                    alreadyPlaced.Add(ghost.Display);
                }
            }

            await LoadTreeAsync();
            RebuildModel();

            if (alreadyPlaced.Count > 0 && saveErrorMessage is null)
            {
                saveErrorMessage = $"Already placed, so skipped: {string.Join(", ", alreadyPlaced)}.";
            }
            else if (saveErrorMessage is null)
            {
                NotificationService.Notify(NotificationSeverity.Success, "Placements saved");
            }
        }
        catch (ActivityDataUnavailableException)
        {
            saveErrorMessage = "Something went wrong saving these placements. Try again.";
            RebuildModel();
        }
        finally
        {
            isSaving = false;
        }
    }

    /// <remarks>
    /// A ghost row just leaves the list - it was never saved, so there's no Api call, dialog or toast. A saved
    /// row writes immediately (decision 2), after whichever guard its <see cref="RemovalImpact"/> calls for:
    /// the last-Placement notice when the Activity has no other saved row, the reap confirm when a Tier would
    /// disappear, otherwise a plain remove with Undo in the toast (ADR-0045's exception). The choice is made
    /// from the loaded tree; Api reaps regardless, so a stale tree can at worst skip a dialog it should have
    /// shown - and Undo re-creates the Tiers by name.
    /// </remarks>
    private async Task RemoveAsync(PlacementLeaf leaf)
    {
        saveErrorMessage = null;

        if (leaf.IsPending)
        {
            ghosts.Remove(leaf.Path);
            RebuildModel();
            return;
        }

        RemovalImpact? impact = leaf.PlacementId is { } id ? model!.DescribeRemoval(id) : null;
        if (impact is null)
        {
            await ReloadAfterRemovalAsync();
            return;
        }

        if (impact.OtherPlacementsOfThisActivity == 0)
        {
            await ShowLastPlacementNoticeAsync();
            return;
        }

        if (impact.Reaped.Count > 0)
        {
            bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>(
                "Remove this placement?", PlacementRemovalConfirmation.ForReap(impact, activity!.Name));
            if (confirmed is not true)
            {
                return;
            }
        }

        isRemoving = true;
        try
        {
            switch (await PlacementClient.DeleteAsync(leaf.PlacementId!.Value, CancellationToken.None))
            {
                case PlacementRemoveOutcome.Forbidden:
                    state = PageState.Denied;
                    return;
                case PlacementRemoveOutcome.LastPlacement:
                    await ShowLastPlacementNoticeAsync();
                    break;
                case PlacementRemoveOutcome.Success when impact.Reaped.Count == 0:
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Info,
                        Summary = $"Removed from {impact.Path.Display}",
                        DetailContent = UndoToast(leaf),
                        Duration = UndoToastDuration
                    });
                    break;
            }

            await ReloadAfterRemovalAsync();
        }
        catch (ActivityDataUnavailableException)
        {
            saveErrorMessage = "Couldn't remove that placement. Try again.";
        }
        finally
        {
            isRemoving = false;
        }
    }

    /// <remarks>
    /// Re-creates the removed Placement by name, then restores its <c>SortOrder</c> (decision 3). A failed
    /// reposition is silent: order is advisory until P5-13 (#98), so the row simply stays at the end of its
    /// bucket. A 409 means it's already back (a double click, or another tab restored it).
    /// </remarks>
    private async Task UndoRemovalAsync(PlacementLeaf removed)
    {
        isRemoving = true;
        try
        {
            PlacementPath path = removed.Path;
            (PlacementWriteOutcome outcome, PlacementDto? created, _) = await PlacementClient.CreateAsync(
                ActivityId, path.Tab, path.SubTab, path.Section, path.SubSection, CancellationToken.None);

            if (outcome == PlacementWriteOutcome.Forbidden)
            {
                state = PageState.Denied;
                return;
            }

            if (outcome == PlacementWriteOutcome.Success && created is not null)
            {
                await PlacementClient.UpdateSortOrderAsync(created.Id, removed.SortOrder, CancellationToken.None);
            }
            else if (outcome == PlacementWriteOutcome.Invalid)
            {
                saveErrorMessage = $"Couldn't restore \"{path.Display}\". Add it again with the builder.";
            }

            await ReloadAfterRemovalAsync();
        }
        catch (ActivityDataUnavailableException)
        {
            saveErrorMessage = "Couldn't restore that placement. Try again.";
        }
        finally
        {
            isRemoving = false;
        }
    }

    /// <remarks>Explain-only until P5-9 (#95) gives the notice a "Delete activity" button to confirm (decision 1).</remarks>
    private async Task ShowLastPlacementNoticeAsync() =>
        await DialogService.OpenAsync<ConfirmDialog>(
            "Last placement", PlacementRemovalConfirmation.ForLastPlacement(hasPendingGhosts: ghosts.Count > 0));

    /// <summary>Reloads the tree from Api after a removal or undo, keeping any pending ghosts.</summary>
    private async Task ReloadAfterRemovalAsync()
    {
        try
        {
            await LoadTreeAsync();
        }
        catch (ActivityDataUnavailableException)
        {
            saveErrorMessage = "Couldn't refresh the placements. Reload the page.";
        }

        RebuildModel();
        await InvokeAsync(StateHasChanged);
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
