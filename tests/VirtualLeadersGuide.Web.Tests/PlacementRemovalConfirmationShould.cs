using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="InfoPageDeleteConfirmationShould"/>'s shape - a pure function, not a rendered component.
/// Covers ADR-0045's "every consequence bullet is conditional" rule for the Placement-removal dialog (P5-12, #97).
/// </remarks>
public class PlacementRemovalConfirmationShould
{
    private static RemovalImpact Impact(IReadOnlyList<TierRef> reaped, IReadOnlyList<TierRef> surviving, int others) =>
        new(new PlacementPath("Morning", null, "Waterfront", "Kayaking"), reaped, surviving, others);

    private static IReadOnlyList<string> Bullets(Dictionary<string, object?> parameters) =>
        (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;

    [Fact]
    public void NameThePathInTheMessageAndLabelTheButtons_WhenBuilt_ForForReap()
    {
        Dictionary<string, object?> parameters = PlacementRemovalConfirmation.ForReap(
            Impact([new TierRef(TierKind.SubSection, "Kayaking")], [], 1), "Canoe Basics");

        Assert.Equal("Remove this placement? Morning › Waterfront › Kayaking", parameters[nameof(ConfirmDialog.Message)]);
        Assert.Equal("Remove", parameters[nameof(ConfirmDialog.ConfirmText)]);
        Assert.Equal("Keep it", parameters[nameof(ConfirmDialog.DismissText)]);
    }

    /// <remarks>Wireframe 2c's copy, with the surviving ancestors named deepest first.</remarks>
    [Fact]
    public void ExplainTheReapedTierTheSurvivorsAndWhatTheActivityKeeps_WhenAllApply_ForForReap()
    {
        IReadOnlyList<string> bullets = Bullets(PlacementRemovalConfirmation.ForReap(
            Impact(
                [new TierRef(TierKind.SubSection, "Kayaking")],
                [new TierRef(TierKind.Tab, "Morning"), new TierRef(TierKind.Section, "Waterfront")],
                2),
            "Canoe Basics"));

        Assert.Equal(
        [
            "This is the only activity in Kayaking, so the sub section disappears from the guide too.",
            "Waterfront and Morning stay — other activities still use them.",
            "Canoe Basics keeps its other 2 placements."
        ], bullets);
    }

    [Fact]
    public void ListEveryReapedTierInOneBullet_WhenSeveralLevelsDisappear_ForForReap()
    {
        IReadOnlyList<string> bullets = Bullets(PlacementRemovalConfirmation.ForReap(
            Impact([new TierRef(TierKind.SubSection, "Kayaking"), new TierRef(TierKind.Section, "Waterfront")], [], 1), "Canoe Basics"));

        Assert.Equal("This is the only activity in Kayaking, so the sub section and section disappear from the guide too.", bullets[0]);
    }

    [Fact]
    public void OmitTheStayLineAndUseTheSingularKeepsLine_WhenNothingSurvivesAndOneOtherRemains_ForForReap()
    {
        IReadOnlyList<string> bullets = Bullets(PlacementRemovalConfirmation.ForReap(
            Impact([new TierRef(TierKind.Tab, "Morning")], [], 1), "Canoe Basics"));

        Assert.Equal(2, bullets.Count);
        Assert.Equal("Canoe Basics keeps its other placement.", bullets[1]);
    }

    [Fact]
    public void OfferToDeleteTheActivityInstead_WhenTheActivityHasNoOtherPlacement_ForForLastPlacement()
    {
        Dictionary<string, object?> parameters = PlacementRemovalConfirmation.ForLastPlacement(
            "Canoe Basics", new ActivityDeletionImpact(1, [new TierRef(TierKind.Tab, "Morning")]), pendingGhosts: 0);

        Assert.Equal(
            "An activity needs at least one placement. Delete Canoe Basics instead?", parameters[nameof(ConfirmDialog.Message)]);
        Assert.Equal("Delete activity", parameters[nameof(ConfirmDialog.ConfirmText)]);
        Assert.Equal("Keep it", parameters[nameof(ConfirmDialog.DismissText)]);
        Assert.False(parameters.ContainsKey(nameof(ConfirmDialog.ShowConfirm)));
        Assert.Equal(
        [
            "Its placement is removed.",
            "The Morning tab disappears from the guide too.",
            "This can't be undone."
        ], Bullets(parameters));
    }

    [Fact]
    public void WarnThatPendingPlacementsAreDiscarded_WhenGhostRowsArePending_ForForLastPlacement()
    {
        IReadOnlyList<string> bullets = Bullets(PlacementRemovalConfirmation.ForLastPlacement(
            "Canoe Basics", new ActivityDeletionImpact(1, []), pendingGhosts: 2));

        Assert.Contains("Your 2 unsaved placements are discarded.", bullets);
    }
}
