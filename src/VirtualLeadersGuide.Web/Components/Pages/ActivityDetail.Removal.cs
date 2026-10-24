using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Radzen;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>
/// The removal half of <see cref="ActivityDetail"/>: removing a saved or pending row and Undo (P5-12, #97),
/// and the last-Placement dialog that offers to delete the whole Activity instead (P5-9, #95). Split out so
/// the page's load/save/leave-guard flow in <c>ActivityDetail.razor.cs</c> stays readable; the two halves
/// share the page's state (<c>model</c>, <c>ghosts</c>, <c>state</c>).
/// </summary>
public partial class ActivityDetail
{
    private bool isRemoving;

    /// <summary>How long the "Removed from …" toast stays up for Undo, in milliseconds - longer than the 3 s default so the button can be reached.</summary>
    private const int UndoToastDuration = 8000;

    /// <remarks>
    /// A ghost row just leaves the list - it was never saved, so there's no Api call, dialog or toast. A saved
    /// row writes immediately (decision 2), after whichever guard its <see cref="RemovalImpact"/> calls for:
    /// the offer to delete the Activity when it has no other saved row, the reap confirm when a Tier would
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
            await OfferActivityDeletionAsync();
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
                case PlacementRemoveOutcome.LastPlacement when await OfferActivityDeletionAsync():
                    return;
                case PlacementRemoveOutcome.LastPlacement:
                    break;
                case PlacementRemoveOutcome.NotFound:
                    saveErrorMessage = "That placement was already removed.";
                    break;
                case PlacementRemoveOutcome.Success when impact.Reaped.Count == 0:
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Info,
                        Summary = $"Removed from {impact.Path.Display}",
                        DetailContent = UndoToastContent(leaf),
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

    /// <summary>The "Removed from …" toast's body: an Undo button (ADR-0045's Placement exception) wired back to <see cref="UndoRemovalAsync"/>. Built with a <see cref="RenderTreeBuilder"/> because the toast renders outside this component's markup.</summary>
    private RenderFragment<NotificationService> UndoToastContent(PlacementLeaf removed) => _ => builder =>
    {
        builder.OpenComponent<UndoToast>(0);
        builder.AddComponentParameter(1, nameof(UndoToast.OnUndo), EventCallback.Factory.Create(this, () => UndoRemovalAsync(removed)));
        builder.CloseComponent();
    };

    /// <summary>
    /// The last-Placement dialog (ADR-0050): removing an Activity's final row would strand it at zero, so this
    /// offers to delete the whole Activity instead and, if confirmed, does (P5-9, #95).
    /// </summary>
    /// <returns><see langword="true"/> when the page is finished - the Activity was deleted, or the caller turned out to have no access - and the caller should stop touching it.</returns>
    /// <remarks>
    /// The dialog's consequences come from the loaded tree, like the reap dialog's; Api reaps regardless, so a
    /// stale tree can at worst under-describe the deletion. Pending ghosts are named in the dialog and
    /// discarded with the Activity - cleared first so leaving doesn't prompt a second time.
    /// </remarks>
    private async Task<bool> OfferActivityDeletionAsync()
    {
        bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>(
            "Last placement",
            PlacementRemovalConfirmation.ForLastPlacement(activity!.Name, model!.DescribeDeletion(ActivityId), ghosts.Count));
        if (confirmed is not true)
        {
            return false;
        }

        isRemoving = true;
        try
        {
            if (await ActivityClient.DeleteAsync(ActivityId, CancellationToken.None) == ActivityWriteOutcome.Forbidden)
            {
                state = PageState.Denied;
                return true;
            }

            ghosts.Clear();
            NotificationService.Notify(NotificationSeverity.Success, "Activity deleted");
            NavigateToList();
            return true;
        }
        catch (ActivityDataUnavailableException)
        {
            saveErrorMessage = ActivityDeleteConfirmation.DeleteFailedMessage;
            return false;
        }
        finally
        {
            isRemoving = false;
        }
    }

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
