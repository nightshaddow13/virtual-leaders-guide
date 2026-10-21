using Bunit;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Renders <see cref="PlacementTree"/> in isolation (P5-12, #97) - the × control on this Activity's rows.
/// What removing means for a saved row versus a ghost is the host's business and is covered by
/// <see cref="ActivityDetailRemovalShould"/>; here only the control itself is pinned: which rows carry it, what
/// it raises, and that <see cref="PlacementTree.IsBusy"/> disables it.
/// </remarks>
public class PlacementTreeShould : BunitContext
{
    private static readonly Guid ThisActivity = Guid.NewGuid();
    private static readonly Guid OtherActivity = Guid.NewGuid();

    private readonly Guid _morning = Guid.NewGuid();
    private readonly Guid _savedPlacement = Guid.NewGuid();

    public PlacementTreeShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    /// <summary>Morning holds this Activity (saved), another Activity, and a pending ghost of this Activity under a new Evening Tab.</summary>
    private IReadOnlyList<TierNode> Roots() => new PlacementTreeModel(
        new PlacementTreeDto
        {
            Tabs = [new TabDto { Id = _morning, Name = "Morning", SortOrder = 0 }],
            Placements =
            [
                new PlacementDto { Id = _savedPlacement, ActivityId = ThisActivity, TabId = _morning, SortOrder = 0 },
                new PlacementDto { Id = Guid.NewGuid(), ActivityId = OtherActivity, TabId = _morning, SortOrder = 1 }
            ]
        },
        ThisActivity, "Canoe Basics", new Dictionary<Guid, string> { [OtherActivity] = "Swim Check" },
        [new PlacementPath("Evening")]).Roots;

    private IRenderedComponent<PlacementTree> RenderPlacementTree(
        Action<PlacementLeaf>? onRemove = null, bool isBusy = false) =>
        Render<PlacementTree>(parameters => parameters
            .Add(c => c.Roots, Roots())
            .Add(c => c.IsBusy, isBusy)
            .Add(c => c.OnRemove, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<PlacementLeaf>(this, leaf => onRemove?.Invoke(leaf))));

    [Fact]
    public void OfferARemoveButtonOnlyOnThisActivitysSavedAndPendingRows_WhenRendered_ForPlacementTree()
    {
        IRenderedComponent<PlacementTree> cut = RenderPlacementTree();

        Assert.Equal(
            ["Remove placement Evening", "Remove placement Morning"],
            cut.FindAll("button[aria-label^='Remove placement']").Select(b => b.GetAttribute("aria-label")!).Order());
    }

    [Fact]
    public void RaiseOnRemoveWithTheSavedRow_WhenItsRemoveButtonIsClicked_ForPlacementTree()
    {
        PlacementLeaf? raised = null;
        IRenderedComponent<PlacementTree> cut = RenderPlacementTree(leaf => raised = leaf);

        cut.Find("button[aria-label='Remove placement Morning']").Click();

        Assert.Equal(_savedPlacement, raised!.PlacementId);
        Assert.False(raised.IsPending);
    }

    [Fact]
    public void RaiseOnRemoveWithThePendingRow_WhenItsRemoveButtonIsClicked_ForPlacementTree()
    {
        PlacementLeaf? raised = null;
        IRenderedComponent<PlacementTree> cut = RenderPlacementTree(leaf => raised = leaf);

        cut.Find("button[aria-label='Remove placement Evening']").Click();

        Assert.True(raised!.IsPending);
        Assert.Null(raised.PlacementId);
    }

    [Fact]
    public void DisableEveryRemoveButton_WhenBusy_ForPlacementTree()
    {
        IRenderedComponent<PlacementTree> cut = RenderPlacementTree(isBusy: true);

        Assert.All(cut.FindAll("button[aria-label^='Remove placement']"), b => Assert.True(b.HasAttribute("disabled")));
    }
}
