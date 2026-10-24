using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Activities;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for removing a Placement (P5-12, #97; wireframe turn 2c)
/// - the one destructive action that doesn't always confirm (ADR-0045's exception). The caller opens the
/// reap dialog only when <see cref="RemovalImpact.Reaped"/> is non-empty, and the last-Placement dialog (which
/// offers to delete the Activity, P5-9 #95) only when <see cref="RemovalImpact.OtherPlacementsOfThisActivity"/>
/// is zero.
/// </summary>
/// <remarks>
/// Every bullet is conditional (ADR-0045): a removal that keeps no ancestor alive carries no "stay" line, and
/// an Activity with no other Placement never reaches the reap dialog at all.
/// </remarks>
internal static class PlacementRemovalConfirmation
{
    /// <summary>Builds the parameters for confirming a removal that empties at least one Tier.</summary>
    /// <param name="impact">What the removal does, from <see cref="PlacementTreeModel.DescribeRemoval"/>.</param>
    /// <param name="activityName">The Activity being removed, named in the "keeps its other placements" line.</param>
    /// <returns>The parameter dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c>.</returns>
    public static Dictionary<string, object?> ForReap(RemovalImpact impact, string activityName)
    {
        var consequences = new List<string>();

        if (impact.Reaped.Count > 0)
        {
            TierRef deepest = impact.Reaped[0];
            consequences.Add($"This is the only activity in {deepest.Name}, so {Describe(impact.Reaped)} from the guide too.");
        }

        if (impact.Surviving.Count > 0)
        {
            string[] names = [.. impact.Surviving.Select(tier => tier.Name).Reverse()];
            consequences.Add(names.Length == 1
                ? $"{names[0]} stays — other activities still use it."
                : $"{JoinNames(names)} stay — other activities still use them.");
        }

        if (impact.OtherPlacementsOfThisActivity > 0)
        {
            consequences.Add(impact.OtherPlacementsOfThisActivity == 1
                ? $"{activityName} keeps its other placement."
                : $"{activityName} keeps its other {impact.OtherPlacementsOfThisActivity} placements.");
        }

        return new Dictionary<string, object?>
        {
            [nameof(ConfirmDialog.Message)] = $"Remove this placement? {impact.Path.Display}",
            [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>)consequences,
            [nameof(ConfirmDialog.ConfirmText)] = "Remove",
            [nameof(ConfirmDialog.DismissText)] = "Keep it"
        };
    }

    /// <summary>
    /// Builds the dialog for an Activity's last Placement (ADR-0050, P5-9 #95): a flat refusal would strand the
    /// user, so it offers to delete the whole Activity instead, carrying the same consequence bullets as the
    /// list's delete confirmation.
    /// </summary>
    /// <param name="activityName">The Activity, named in the question.</param>
    /// <param name="impact">What deleting the Activity does, from <see cref="PlacementTreeModel.DescribeDeletion"/>.</param>
    /// <param name="pendingGhosts">How many unsaved placements are waiting - they're discarded along with the Activity, and the bullet says so.</param>
    /// <returns>The parameter dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c>.</returns>
    public static Dictionary<string, object?> ForLastPlacement(
        string activityName, ActivityDeletionImpact impact, int pendingGhosts) => new()
    {
        [nameof(ConfirmDialog.Message)] = $"An activity needs at least one placement. Delete {activityName} instead?",
        [nameof(ConfirmDialog.Consequences)] = ActivityDeleteConfirmation.Consequences(impact, pendingGhosts),
        [nameof(ConfirmDialog.ConfirmText)] = "Delete activity",
        [nameof(ConfirmDialog.DismissText)] = "Keep it"
    };

    private static string Describe(IReadOnlyList<TierRef> reaped)
    {
        string[] nouns = [.. reaped.Select(tier => Noun(tier.Kind))];
        return $"the {JoinNames(nouns)} {Disappear(nouns.Length)}";
    }

    /// <summary>Joins names as prose - "A", "A and B", "A, B and C" - the one list shape both removal dialogs' copy uses.</summary>
    /// <param name="names">The names to join; at least one.</param>
    internal static string JoinNames(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    /// <summary>The verb agreeing with <paramref name="count"/> subjects - "disappears" for one, "disappear" for more.</summary>
    /// <param name="count">How many things are disappearing.</param>
    internal static string Disappear(int count) => count == 1 ? "disappears" : "disappear";

    /// <summary>The lower-case name of a Tier level, as the dialogs say it ("sub section").</summary>
    /// <param name="kind">Which level.</param>
    internal static string Noun(TierKind kind) => kind switch
    {
        TierKind.Tab => "tab",
        TierKind.SubTab => "sub tab",
        TierKind.Section => "section",
        _ => "sub section"
    };
}
