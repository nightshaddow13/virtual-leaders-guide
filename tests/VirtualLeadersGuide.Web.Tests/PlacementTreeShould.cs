using Bunit;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Renders <see cref="PlacementTree"/> in isolation - the × (P5-12, #97) and ↑/↓ (P5-13, #98) controls on this
/// Activity's rows. What they do is the host's business and is covered by
/// <see cref="ActivityDetailRemovalShould"/> and <see cref="ActivityDetailReorderShould"/>; here only the
/// controls themselves are pinned: which rows carry them, what they raise, and that
/// <see cref="PlacementTree.IsBusy"/> disables them.
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

    [Fact]
    public void OfferArrowsOnlyOnThisActivitysSavedRows_WhenRendered_ForPlacementTree()
    {
        IRenderedComponent<PlacementTree> cut = RenderPlacementTree();

        Assert.Equal(
            ["Move Morning up", "Move Morning down"],
            cut.FindAll("button[aria-label^='Move ']").Select(b => b.GetAttribute("aria-label")!));
    }

    /// <remarks>This Activity is first of the two saved rows in Morning.</remarks>
    [Fact]
    public void DisableUpAndRaiseOnMoveDownward_WhenTheRowIsFirst_ForPlacementTree()
    {
        PlacementMove? raised = null;
        IRenderedComponent<PlacementTree> cut = Render<PlacementTree>(parameters => parameters
            .Add(c => c.Roots, Roots())
            .Add(c => c.OnMove, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<PlacementMove>(this, move => raised = move)));
        Assert.True(cut.Find("button[aria-label='Move Morning up']").HasAttribute("disabled"));

        cut.Find("button[aria-label='Move Morning down']").Click();

        Assert.Equal((_savedPlacement, PlacementDirection.Down), (raised!.Leaf.PlacementId!.Value, raised.Direction));
    }

    [Fact]
    public void DisableTheArrowsButNotRemove_WhenStale_ForPlacementTree()
    {
        IRenderedComponent<PlacementTree> cut = Render<PlacementTree>(parameters => parameters
            .Add(c => c.Roots, Roots())
            .Add(c => c.IsStale, true));

        Assert.All(cut.FindAll("button[aria-label^='Move ']"), b => Assert.True(b.HasAttribute("disabled")));
        Assert.All(cut.FindAll("button[aria-label^='Remove placement']"), b => Assert.False(b.HasAttribute("disabled")));
    }

    [Fact]
    public void DisableBothArrows_WhenBusy_ForPlacementTree()
    {
        IRenderedComponent<PlacementTree> cut = RenderPlacementTree(isBusy: true);

        Assert.All(cut.FindAll("button[aria-label^='Move ']"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void RenderTheAnnouncementAndTheNarrowWindowHint_WhenRendered_ForPlacementTree()
    {
        IRenderedComponent<PlacementTree> cut = Render<PlacementTree>(parameters => parameters
            .Add(c => c.Roots, Roots())
            .Add(c => c.Announcement, "Moved to position 1 of 2."));

        Assert.Equal("Moved to position 1 of 2.", cut.Find("[role='status']").TextContent);
        Assert.Contains("Reorder from a wider window.", cut.Markup, StringComparison.Ordinal);
    }
}
