using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Activities;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for deleting an Activity (P5-9, #95) - the one source of
/// the consequence bullets for both entry points, the list row's delete icon (<see cref="ForList"/>) and the
/// last-Placement dialog (<see cref="PlacementRemovalConfirmation.ForLastPlacement"/>), so the two can't drift
/// on the copy ADR-0045 governs.
/// </summary>
/// <remarks>
/// Every bullet is conditional (ADR-0045): an Activity with no Placements carries no "placements are removed"
/// line, nothing reaped means no "disappears" line, and the discard line appears only while unsaved rows are
/// pending. A <see langword="null"/> impact - the Placement tree failed to load - degrades the Placement and
/// Tier bullets to one explanatory line rather than blocking the dialog from opening, since an Admin's
/// ability to delete a record shouldn't depend on advisory data (ADR-0045).
/// </remarks>
internal static class ActivityDeleteConfirmation
{
    /// <summary>What both entry points tell the user when the delete call itself fails (the Activity store didn't answer), so the copy can't drift between them.</summary>
    public const string DeleteFailedMessage = "Something went wrong deleting this Activity. Try again.";

    /// <summary>Builds the parameters for the list row's delete confirmation.</summary>
    /// <param name="activityName">The Activity being deleted, named in the message.</param>
    /// <param name="impact">What the deletion does, from <see cref="PlacementTreeModel.DescribeDeletion"/>, or <see langword="null"/> if the tree couldn't be loaded.</param>
    /// <returns>The parameter dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c>.</returns>
    public static Dictionary<string, object?> ForList(string activityName, ActivityDeletionImpact? impact) => new()
    {
        [nameof(ConfirmDialog.Message)] = $"Delete {activityName}?",
        [nameof(ConfirmDialog.Consequences)] = Consequences(impact, pendingGhosts: 0)
    };

    /// <summary>Builds the consequence bullets shared by every Activity-deletion dialog.</summary>
    /// <param name="impact">What the deletion does, or <see langword="null"/> if the tree couldn't be loaded.</param>
    /// <param name="pendingGhosts">How many unsaved placement rows the Activity page holds - they're discarded with it.</param>
    /// <returns>The bullets, in the order they're shown.</returns>
    public static IReadOnlyList<string> Consequences(ActivityDeletionImpact? impact, int pendingGhosts)
    {
        var consequences = new List<string>();

        if (impact is null)
        {
            consequences.Add("Its placements couldn't be loaded, so this can't say what else disappears.");
        }
        else
        {
            if (impact.PlacementCount > 0)
            {
                consequences.Add(impact.PlacementCount == 1
                    ? "Its placement is removed."
                    : $"Its {impact.PlacementCount} placements are removed.");
            }

            if (impact.Reaped.Count > 0)
            {
                consequences.Add(DescribeReaped(impact.Reaped));
            }
        }

        if (pendingGhosts > 0)
        {
            consequences.Add(pendingGhosts == 1
                ? "Your unsaved placement is discarded."
                : $"Your {pendingGhosts} unsaved placements are discarded.");
        }

        consequences.Add("This can't be undone.");
        return consequences;
    }

    /// <remarks>Shallowest level first, so it reads "the Morning tab and Waterfront section" rather than inside-out.</remarks>
    private static string DescribeReaped(IReadOnlyList<TierRef> reaped)
    {
        string[] named = [.. reaped.OrderBy(tier => tier.Kind).Select(tier => $"{tier.Name} {PlacementRemovalConfirmation.Noun(tier.Kind)}")];
        return $"The {PlacementRemovalConfirmation.JoinNames(named)} {PlacementRemovalConfirmation.Disappear(named.Length)} from the guide too.";
    }
}
