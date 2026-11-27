using Microsoft.AspNetCore.Components.Routing;
using Radzen;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>The saving half of <see cref="ActivityDetail"/>: the one Save, and the guard on leaving before it (P5-23, #206).</summary>
/// <remarks>
/// One Save is not one transaction: its two parts succeed or fail on their own (ADR-0072's P5-23 amendment).
/// Leaving is guarded three ways: navigation the circuit performs by <see cref="ConfirmLeavingAsync"/>; link
/// clicks, which the circuit never hears about, by <c>LinkGuard</c> turning them into such navigation; and
/// reload, tab close and external links by a <c>NavigationLock</c>, which can only raise the browser's own prompt.
/// </remarks>
public partial class ActivityDetail
{
    private bool isSaving;

    /// <summary>Completes when the Save in flight does, so Undo can wait its turn.</summary>
    private Task saveFinished = Task.CompletedTask;

    /// <summary>Set once the user has chosen to leave, or the page is gone: nothing further gets sent.</summary>
    private bool abandoned;

    private IDisposable? locationChangingRegistration;

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
        if (leave is true)
        {
            abandoned = true;
        }
        else
        {
            context.PreventNavigation();
        }
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
    /// Any failure is also checked against the Activity still existing, since "try again" is wrong if it doesn't.
    /// </remarks>
    private async Task SaveAsync()
    {
        if (WriteBlocked || !HasUnsavedChanges)
        {
            return;
        }

        isSaving = true;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        saveFinished = finished.Task;
        saveErrorMessage = null;
        var problems = new List<string>();

        try
        {
            bool detailsSaved = IsDirty && await SaveDetailsAsync(problems);
            bool placementsSaved = !permissionLost && !abandoned && ghosts.Count > 0 && await SaveGhostsAsync(problems);

            if (abandoned)
            {
                return;
            }

            if (problems.Count > 0 && !permissionLost && await ActivityIsGoneAsync())
            {
                ghosts.Clear();
                ResetForm();
                state = PageState.NotFound;
                return;
            }

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
            finished.SetResult();
        }
    }

    /// <summary>PATCHes the Name and Description, adopting them as the saved <see cref="activity"/> on success.</summary>
    /// <param name="problems">Gets a line to show the user if the write failed.</param>
    /// <returns>Whether they were saved.</returns>
    private async Task<bool> SaveDetailsAsync(List<string> problems)
    {
        string name = form.NameOrEmpty;
        string description = form.DescriptionOrEmpty;

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
    /// satisfied rather than treated as a failure, and one whose screen an InfoPage fills (P5-24, #208) is dropped
    /// and reported. When only some got saved, the saved ones are named too.
    /// Leaving mid-save stops the loop; the request already sent can't be recalled.
    /// </remarks>
    /// <returns>Whether every ghost was newly saved - <see langword="false"/> if any was rejected, skipped or never sent.</returns>
    private async Task<bool> SaveGhostsAsync(List<string> problems)
    {
        var saved = new List<string>();
        var alreadyPlaced = new List<string>();
        var onInfoPageScreens = new List<string>();
        bool stopped = false;

        try
        {
            foreach (PlacementPath ghost in ghosts.ToList())
            {
                if (abandoned)
                {
                    return false;
                }

                (PlacementWriteOutcome outcome, PlacementDto? created, _) = await PlacementClient.CreateAsync(
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
                if (outcome == PlacementWriteOutcome.Conflict)
                {
                    alreadyPlaced.Add(ghost.Display);
                    continue;
                }

                if (outcome == PlacementWriteOutcome.SlotHasInfoPage)
                {
                    onInfoPageScreens.Add(ghost.Display);
                    continue;
                }

                saved.Add(ghost.Display);
                savedSinceLoad.Add((created!.Id, ghost));
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

        if (onInfoPageScreens.Count > 0)
        {
            problems.Add($"Skipped, because an Info page is shown there: {string.Join(", ", onInfoPageScreens)}.");
        }

        bool allSaved = !stopped && alreadyPlaced.Count == 0 && onInfoPageScreens.Count == 0;
        if (!allSaved && saved.Count > 0)
        {
            problems.Add($"Saved: {string.Join(", ", saved)}.");
        }

        return allSaved;
    }

    /// <returns><see langword="true"/> only when Api says the Activity doesn't exist; a read that fails proves nothing.</returns>
    private async Task<bool> ActivityIsGoneAsync()
    {
        try
        {
            (ActivityReadOutcome outcome, _) = await ActivityClient.GetActivityAsync(ActivityId, CancellationToken.None);
            return outcome == ActivityReadOutcome.NotFound;
        }
        catch (ActivityDataUnavailableException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        abandoned = true;
        locationChangingRegistration?.Dispose();
    }
}
