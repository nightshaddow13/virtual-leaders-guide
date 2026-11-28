using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for deleting an InfoPage (P5-17, #22) - shared by
/// <c>InfoPageList.razor.cs</c>'s grid row action and <c>InfoPageEditor.razor.cs</c>'s delete action so the
/// two call sites can't drift on the consequence copy ADR-0045 governs.
/// </summary>
/// <remarks>
/// Every bullet is conditional (ADR-0045), mirroring <see cref="ActivityDeleteConfirmation"/> (P5-25, #209): an
/// unplaced InfoPage carries no "removed from" line and nothing reaped means no "disappears" line. A
/// <see langword="null"/> impact - the Placement tree failed to load - degrades both to one explanatory line
/// rather than blocking the dialog.
/// </remarks>
internal static class InfoPageDeleteConfirmation
{
    /// <summary>What deleting an InfoPage would do, from a fresh read of the Placement tree so the dialog describes the Event as it is now (P5-25, #209).</summary>
    /// <param name="placementClient">Reads the Event's tree.</param>
    /// <param name="eventId">The InfoPage's Event.</param>
    /// <param name="infoPageId">The InfoPage about to be deleted.</param>
    /// <returns>The impact, or <see langword="null"/> if the tree couldn't be read - advisory data, so the caller still opens the dialog without it (ADR-0045).</returns>
    public static async Task<InfoPageDeletionImpact?> LoadImpactAsync(ApiPlacementClient placementClient, Guid eventId, Guid infoPageId)
    {
        try
        {
            PlacementTreeDto tree = await placementClient.GetTreeForEventAsync(eventId, CancellationToken.None);
            return PlacementTreeModel.DescribeInfoPageDeletion(tree, infoPageId);
        }
        catch (ActivityDataUnavailableException)
        {
            return null;
        }
    }

    /// <summary>Builds the parameters dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c> when confirming an InfoPage's deletion.</summary>
    /// <param name="title">The InfoPage's display Title, named in the dialog's message.</param>
    /// <param name="impact">What the deletion does, from <see cref="PlacementTreeModel.DescribeInfoPageDeletion"/>, or <see langword="null"/> if the tree couldn't be loaded.</param>
    /// <returns>The parameter dictionary.</returns>
    public static Dictionary<string, object?> BuildDialogParameters(string title, InfoPageDeletionImpact? impact) => new()
    {
        [nameof(ConfirmDialog.Message)] = $"Delete {title}?",
        [nameof(ConfirmDialog.Consequences)] = Consequences(impact)
    };

    private static IReadOnlyList<string> Consequences(InfoPageDeletionImpact? impact)
    {
        var consequences = new List<string>();

        if (impact is null)
        {
            consequences.Add("Its placement couldn't be loaded, so this can't say what else disappears.");
        }
        else
        {
            if (impact.Path is not null)
            {
                consequences.Add($"It's removed from {impact.Path.Display}.");
            }

            if (impact.Reaped.Count > 0)
            {
                consequences.Add(PlacementRemovalConfirmation.DescribeReaped(impact.Reaped));
            }
        }

        consequences.Add("This can't be undone.");
        return consequences;
    }
}
