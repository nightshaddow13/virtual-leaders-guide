using Radzen;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>
/// The removal half of <see cref="ActivityDetail"/> (P5-12, #97): removing a saved or pending row, Undo, and
/// the last-Placement notice. Split out so the page's load/save/leave-guard flow in <c>ActivityDetail.razor.cs</c>
/// stays readable; the two halves share the page's state (<c>model</c>, <c>ghosts</c>, <c>state</c>).
/// </summary>
public partial class ActivityDetail
{
    private bool isRemoving;

    /// <summary>How long the "Removed from …" toast stays up for Undo, in milliseconds - longer than the 3 s default so the button can be reached.</summary>
    private const int UndoToastDuration = 8000;

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
            await RefreshTreeAsync();
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

            await RefreshTreeAsync();
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

            await RefreshTreeAsync();
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
    private async Task RefreshTreeAsync()
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
}
