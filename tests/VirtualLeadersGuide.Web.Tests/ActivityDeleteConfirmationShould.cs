using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="PlacementRemovalConfirmationShould"/>'s shape - a pure function, not a rendered component.
/// Covers ADR-0045's "every consequence bullet is conditional" rule for the Activity-deletion dialogs (P5-9, #95).
/// </remarks>
public class ActivityDeleteConfirmationShould
{
    private static IReadOnlyList<string> Bullets(Dictionary<string, object?> parameters) =>
        (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;

    [Fact]
    public void NameTheActivityInTheMessageAndKeepTheDefaultButtons_WhenBuilt_ForForList()
    {
        Dictionary<string, object?> parameters = ActivityDeleteConfirmation.ForList(
            "Canoe Basics", new ActivityDeletionImpact(2, []));

        Assert.Equal("Delete Canoe Basics?", parameters[nameof(ConfirmDialog.Message)]);
        Assert.False(parameters.ContainsKey(nameof(ConfirmDialog.ConfirmText)));
        Assert.False(parameters.ContainsKey(nameof(ConfirmDialog.DismissText)));
    }

    [Fact]
    public void CountThePlacementsAndEndWithTheIrreversibilityBullet_WhenNothingIsReaped_ForConsequences()
    {
        IReadOnlyList<string> bullets = ActivityDeleteConfirmation.Consequences(new ActivityDeletionImpact(3, []), pendingGhosts: 0);

        Assert.Equal(["Its 3 placements are removed.", "This can't be undone."], bullets);
    }

    [Fact]
    public void UseTheSingularPlacementLine_WhenThereIsExactlyOne_ForConsequences()
    {
        IReadOnlyList<string> bullets = ActivityDeleteConfirmation.Consequences(new ActivityDeletionImpact(1, []), pendingGhosts: 0);

        Assert.Equal("Its placement is removed.", bullets[0]);
    }

    [Fact]
    public void OmitThePlacementLine_WhenTheActivityIsNotPlacedAnywhere_ForConsequences()
    {
        IReadOnlyList<string> bullets = ActivityDeleteConfirmation.Consequences(new ActivityDeletionImpact(0, []), pendingGhosts: 0);

        Assert.Equal(["This can't be undone."], bullets);
    }

    [Fact]
    public void NameTheReapedTierWithItsLevel_WhenOneDisappears_ForConsequences()
    {
        IReadOnlyList<string> bullets = ActivityDeleteConfirmation.Consequences(
            new ActivityDeletionImpact(1, [new TierRef(TierKind.SubSection, "Kayaking")]), pendingGhosts: 0);

        Assert.Equal("The Kayaking sub section disappears from the guide too.", bullets[1]);
    }

    /// <remarks>The impact arrives deepest level first; the sentence reads shallowest first, so it walks the path top-down.</remarks>
    [Fact]
    public void ListEveryReapedTierShallowestFirstInOneBullet_WhenSeveralLevelsDisappear_ForConsequences()
    {
        IReadOnlyList<string> bullets = ActivityDeleteConfirmation.Consequences(
            new ActivityDeletionImpact(1,
            [
                new TierRef(TierKind.SubSection, "Kayaking"),
                new TierRef(TierKind.Section, "Waterfront"),
                new TierRef(TierKind.Tab, "Morning")
            ]),
            pendingGhosts: 0);

        Assert.Equal("The Morning tab, Waterfront section and Kayaking sub section disappear from the guide too.", bullets[1]);
    }

    [Theory]
    [InlineData(1, "Your unsaved placement is discarded.")]
    [InlineData(2, "Your 2 unsaved placements are discarded.")]
    public void WarnThatPendingPlacementsAreDiscarded_WhenGhostRowsArePending_ForConsequences(int pendingGhosts, string expected)
    {
        IReadOnlyList<string> bullets = ActivityDeleteConfirmation.Consequences(new ActivityDeletionImpact(1, []), pendingGhosts);

        Assert.Contains(expected, bullets);
    }

    [Fact]
    public void OmitTheDiscardLine_WhenNoGhostRowsArePending_ForConsequences()
    {
        IReadOnlyList<string> bullets = ActivityDeleteConfirmation.Consequences(new ActivityDeletionImpact(1, []), pendingGhosts: 0);

        Assert.DoesNotContain(bullets, bullet => bullet.Contains("unsaved", StringComparison.Ordinal));
    }

    /// <remarks>ADR-0045: a failed advisory fetch degrades the bullets rather than blocking the dialog.</remarks>
    [Fact]
    public void DegradeThePlacementAndTierBullets_WhenTheImpactCouldNotBeLoaded_ForConsequences()
    {
        IReadOnlyList<string> bullets = ActivityDeleteConfirmation.Consequences(null, pendingGhosts: 0);

        Assert.Equal(
            ["Its placements couldn't be loaded, so this can't say what else disappears.", "This can't be undone."], bullets);
    }
}
