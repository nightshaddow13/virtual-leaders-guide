using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.InfoPages;

namespace VirtualLeadersGuide.Web.Components.Pages;

/// <summary>
/// The editor's Placement section (P5-24, #208). A Tab/Sub Tab pick is written by Save changes, after the page
/// itself; Remove acts at once.
/// </summary>
public partial class InfoPageEditor
{
    [Inject]
    private ApiPlacementClient PlacementClient { get; set; } = default!;

    [Inject]
    private ApiInfoPagePlacementClient InfoPagePlacementClient { get; set; } = default!;

    private InfoPagePlacementPicker? picker;
    private string? placementTab;
    private string? placementSubTab;
    private bool isChangingPlacement;

    private bool HasPlacementTab => !string.IsNullOrWhiteSpace(placementTab);

    /// <remarks>Titles are only fetched when some other InfoPage is placed - nothing else needs them.</remarks>
    private async Task LoadPlacementAsync()
    {
        PlacementTreeDto tree = await PlacementClient.GetTreeForEventAsync(EventId, CancellationToken.None);
        var titles = new Dictionary<Guid, string>();
        if (tree.InfoPagePlacements.Any(p => p.InfoPageId != InfoPageId))
        {
            (IReadOnlyList<InfoPageDto> infoPages, _) =
                await InfoPageClient.GetInfoPagesForEventAsync(EventId, 1, 9999, CancellationToken.None);
            titles = infoPages.ToDictionary(p => p.Id, p => p.Title);
        }

        picker = new InfoPagePlacementPicker(tree, InfoPageId!.Value, titles);
    }

    private void OnPlacementTabChanged(string? value)
    {
        placementTab = value;
        placementSubTab = null;
    }

    private void ChangePlacement()
    {
        placementTab = picker!.Current!.Tab;
        placementSubTab = picker.Current.SubTab;
        isChangingPlacement = true;
    }

    private void KeepPlacement()
    {
        placementTab = null;
        placementSubTab = null;
        isChangingPlacement = false;
    }

    /// <summary>Places or moves the InfoPage if the user picked somewhere new.</summary>
    /// <returns>Whether Save changes may carry on to the list; <see langword="false"/> leaves the user here with the reason shown.</returns>
    private async Task<bool> SavePlacementAsync(Guid infoPageId)
    {
        if (!HasPlacementTab)
        {
            return true;
        }

        string? subTab = string.IsNullOrWhiteSpace(placementSubTab) ? null : placementSubTab.Trim();
        var wanted = new PlacementPath(placementTab!.Trim(), subTab, null, null);
        if (wanted.Equals(picker!.Current))
        {
            return true;
        }

        InfoPagePlacementWriteOutcome outcome = picker.PlacementId is { } placementId
            ? await InfoPagePlacementClient.MoveAsync(placementId, wanted.Tab, wanted.SubTab, CancellationToken.None)
            : await InfoPagePlacementClient.CreateAsync(infoPageId, wanted.Tab, wanted.SubTab, CancellationToken.None);

        if (outcome == InfoPagePlacementWriteOutcome.Success)
        {
            return true;
        }

        if (outcome == InfoPagePlacementWriteOutcome.Forbidden)
        {
            permissionLostMessage = "You no longer have permission to edit this Info page.";
            return false;
        }

        string reason = outcome switch
        {
            InfoPagePlacementWriteOutcome.SlotHasActivities => $"{wanted.Display} already shows Activities. Pick a different Tab or Sub Tab.",
            InfoPagePlacementWriteOutcome.SlotHasOtherInfoPage => $"{wanted.Display} already shows another Info page. Pick a different Tab or Sub Tab.",
            InfoPagePlacementWriteOutcome.AlreadyPlaced => "this Info page was already placed elsewhere.",
            InfoPagePlacementWriteOutcome.NotFound => "its placement was removed elsewhere. Save again to place it.",
            _ => $"it couldn't be placed on {wanted.Display}. Check the Tab and Sub Tab."
        };
        saveErrorMessage = $"Title and content saved, but not the placement: {reason}";

        await LoadPlacementAsync();
        if (outcome == InfoPagePlacementWriteOutcome.AlreadyPlaced)
        {
            KeepPlacement();
        }
        else if (outcome == InfoPagePlacementWriteOutcome.NotFound)
        {
            isChangingPlacement = false;
        }

        return false;
    }

    private async Task RemovePlacementAsync()
    {
        if (picker is not { PlacementId: { } placementId, Current: { } current })
        {
            return;
        }

        saveErrorMessage = null;
        IReadOnlyList<TierRef> reaped = picker.DescribeRemoval();
        if (reaped.Count > 0)
        {
            bool? confirmed = await DialogService.OpenAsync<ConfirmDialog>(
                "Remove placement?", PlacementRemovalConfirmation.ForInfoPage(current, reaped));
            if (confirmed is not true)
            {
                return;
            }
        }

        isSaving = true;
        try
        {
            InfoPagePlacementWriteOutcome outcome = await InfoPagePlacementClient.DeleteAsync(placementId, CancellationToken.None);
            if (outcome == InfoPagePlacementWriteOutcome.Forbidden)
            {
                permissionLostMessage = "You no longer have permission to edit this Info page.";
                return;
            }

            await LoadPlacementAsync();
            KeepPlacement();
        }
        catch (Exception ex) when (ex is ActivityDataUnavailableException or InfoPageDataUnavailableException)
        {
            saveErrorMessage = "Something went wrong removing this placement. Try again.";
        }
        finally
        {
            isSaving = false;
        }
    }
}
