using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.InfoPages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="EventDeleteConfirmationShould"/>'s shape - a pure function, not a rendered component.
/// </remarks>
public class InfoPageDeleteConfirmationShould
{
    private static readonly PlacementPath Packing = new("Logistics", "Packing", null, null);

    [Fact]
    public void SetTheMessageToDeleteTheInfoPagesTitle_WhenBuilt_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            InfoPageDeleteConfirmation.BuildDialogParameters("Packing List", new InfoPageDeletionImpact(null, []));

        Assert.Equal("Delete Packing List?", parameters[nameof(ConfirmDialog.Message)]);
    }

    [Fact]
    public void IncludeOnlyTheIrreversibilityBullet_WhenTheInfoPageIsNotPlaced_ForBuildDialogParameters() =>
        Assert.Equal(["This can't be undone."], Consequences(new InfoPageDeletionImpact(null, [])));

    [Fact]
    public void SayWhereItIsRemovedFrom_WhenPlacedAndNothingIsReaped_ForBuildDialogParameters() =>
        Assert.Equal(
            ["It's removed from Logistics › Packing.", "This can't be undone."],
            Consequences(new InfoPageDeletionImpact(Packing, [])));

    [Fact]
    public void NameTheReapedTiersShallowestFirst_WhenTheDeletionEmptiesThem_ForBuildDialogParameters() =>
        Assert.Equal(
            [
                "It's removed from Logistics › Packing.",
                "The Logistics tab and Packing sub tab disappear from the guide too.",
                "This can't be undone."
            ],
            Consequences(new InfoPageDeletionImpact(
                Packing, [new TierRef(TierKind.SubTab, "Packing"), new TierRef(TierKind.Tab, "Logistics")])));

    [Fact]
    public void DegradeThePlacementBullets_WhenTheImpactCouldNotBeLoaded_ForBuildDialogParameters() =>
        Assert.Equal(
            ["Its placement couldn't be loaded, so this can't say what else disappears.", "This can't be undone."],
            Consequences(null));

    private static IReadOnlyList<string> Consequences(InfoPageDeletionImpact? impact) =>
        (IReadOnlyList<string>)InfoPageDeleteConfirmation.BuildDialogParameters("Packing List", impact)[nameof(ConfirmDialog.Consequences)]!;
}
