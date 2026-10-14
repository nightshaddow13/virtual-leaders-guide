using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Activities;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for removing a Placement (P5-12, #97; wireframe turn 2c)
/// - the one destructive action that doesn't always confirm (ADR-0045's exception). The caller opens the
/// reap dialog only when <see cref="RemovalImpact.Reaped"/> is non-empty, and the last-Placement notice only
/// when <see cref="RemovalImpact.OtherPlacementsOfThisActivity"/> is zero.
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

    /// <summary>Builds the explain-only notice for an Activity's last Placement (ADR-0050).</summary>
    /// <param name="hasPendingGhosts">Whether unsaved placements are waiting - adds the "save them first" line, since the guard counts saved rows only.</param>
    public static Dictionary<string, object?> ForLastPlacement(bool hasPendingGhosts)
    {
        var consequences = new List<string> { "Add another placement first, or delete the activity instead." };
        if (hasPendingGhosts)
        {
            consequences.Add("Save your pending placements first, then remove this one.");
        }

        return new Dictionary<string, object?>
        {
            [nameof(ConfirmDialog.Message)] = "An activity needs at least one placement.",
            [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>)consequences,
            [nameof(ConfirmDialog.ShowConfirm)] = false,
            [nameof(ConfirmDialog.DismissText)] = "OK"
        };
    }

    private static string Describe(IReadOnlyList<TierRef> reaped)
    {
        string[] nouns = [.. reaped.Select(tier => Noun(tier.Kind))];
        return nouns.Length == 1
            ? $"the {nouns[0]} disappears"
            : $"the {string.Join(", ", nouns[..^1])} and {nouns[^1]} disappear";
    }

    private static string JoinNames(string[] names) =>
        $"{string.Join(", ", names[..^1])} and {names[^1]}";

    private static string Noun(TierKind kind) => kind switch
    {
        TierKind.Tab => "tab",
        TierKind.SubTab => "sub tab",
        TierKind.Section => "section",
        _ => "sub section"
    };
}
