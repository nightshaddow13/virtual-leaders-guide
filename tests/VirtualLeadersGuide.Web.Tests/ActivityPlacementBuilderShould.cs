using Bunit;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Component coverage for <see cref="PlacementBuilder"/> (P5-11, #96). Every answer it shows comes from
/// <see cref="PlacementTreeModel"/>, which <see cref="PlacementTreeModelShould"/> covers exhaustively - these
/// tests only prove the UI is wired to those answers. <c>RadzenAutoComplete</c> is driven as a plain input
/// (<c>Find("#...").Change(...)</c>), the same way <see cref="FacilityEditorShould"/> does: no test in this
/// project opens a Radzen popup, and <c>JSInterop</c> must be <see cref="JSRuntimeMode.Loose"/> for its popup call.
/// </remarks>
public class ActivityPlacementBuilderShould : BunitContext
{
    private static readonly Guid ThisActivity = Guid.NewGuid();

    public ActivityPlacementBuilderShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void KeepPlaceItDisabledAndLockSubSection_WhenNothingHasBeenTyped_ForRender()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());

        Assert.True(cut.Find(".pb-place").HasAttribute("disabled"));
        Assert.Contains("pick a section first", cut.Markup, StringComparison.Ordinal);
    }

    /// <remarks>The two chains skip independently (CONTEXT.md): a Tab alone unlocks Sub Tab <em>and</em> Section.</remarks>
    [Fact]
    public void UnlockSubTabAndSectionButNotSubSection_WhenOnlyATabIsTyped_ForOnTabChanged()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());

        cut.Find("#PlacementTab").Change("Afternoon");

        Assert.NotEmpty(cut.FindAll("#PlacementSubTab"));
        Assert.NotEmpty(cut.FindAll("#PlacementSection"));
        Assert.Empty(cut.FindAll("#PlacementSubSection"));
        Assert.Contains("pick a section first", cut.Markup, StringComparison.Ordinal);
        Assert.False(cut.Find(".pb-place").HasAttribute("disabled"));
    }

    [Fact]
    public void UnlockSubSection_WhenASectionIsTyped_ForOnSectionChanged()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());

        cut.Find("#PlacementTab").Change("Morning");
        cut.Find("#PlacementSection").Change("Waterfront");

        Assert.NotEmpty(cut.FindAll("#PlacementSubSection"));
    }

    [Fact]
    public void ShowTheCreateLineAndNewBadges_WhenTheTypedNamesMatchNothing_ForRender()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());

        cut.Find("#PlacementTab").Change("Evening");

        Assert.Contains("Create tab", cut.Find(".ptf-create").TextContent, StringComparison.Ordinal);
        Assert.Contains("Evening", cut.Find(".ptf-create").TextContent, StringComparison.Ordinal);
        Assert.Contains("Evening", cut.Find(".pb-chip-new").TextContent, StringComparison.Ordinal);
        Assert.Equal("NEW", cut.Find(".pb-chip-new .pb-new").TextContent.Trim());
    }

    [Fact]
    public void ShowNoCreateLineAndNoNewBadge_WhenTheTypedNameMatchesAnExistingTab_ForRender()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());

        cut.Find("#PlacementTab").Change("morning");

        Assert.Empty(cut.FindAll(".ptf-create"));
        Assert.Empty(cut.FindAll(".pb-chip-new"));
        Assert.Equal("Morning", cut.Find(".pb-chip").TextContent.Trim());
    }

    [Fact]
    public void NudgeTowardTheExistingSpelling_WhenTheTypedNameDiffersOnlyByCase_ForRender()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());

        cut.Find("#PlacementTab").Change("morning");

        Assert.Contains("Did you mean", cut.Find(".ptf-nudge").TextContent, StringComparison.Ordinal);
        Assert.Contains("Morning", cut.Find(".ptf-nudge").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WarnAndDisablePlaceIt_WhenThisActivityIsAlreadyPlacedAtThePath_ForRender()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());

        cut.Find("#PlacementTab").Change("MORNING");

        Assert.Contains("already placed here", cut.Find(".pb-duplicate").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find(".pb-place").HasAttribute("disabled"));
    }

    /// <remarks>Decision 4: a not-yet-saved ghost counts as existing, so repeating its path is flagged before any Save.</remarks>
    [Fact]
    public void WarnAboutAPendingGhostsPath_WhenTheHostRerendersWithTheGhostAdded_ForOnParametersSet()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());
        cut.Find("#PlacementTab").Change("Evening");
        Assert.Empty(cut.FindAll(".pb-duplicate"));

        cut.Render(parameters => parameters.Add(c => c.Model, Model(new PlacementPath("Evening"))));

        Assert.Contains("already placed here", cut.Find(".pb-duplicate").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void RaiseOnPlaceWithTheCanonicalPathAndResetTheFields_WhenPlaceItIsClicked_ForPlaceAsync()
    {
        PlacementPath? placed = null;
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model(), path => placed = path);

        cut.Find("#PlacementTab").Change("morning");
        cut.Find("#PlacementSection").Change("Waterfront");
        cut.Find(".pb-place").Click();

        Assert.Equal("Morning › Waterfront", placed!.Display);
        Assert.Empty(cut.FindAll(".pb-path"));
        Assert.True(cut.Find(".pb-place").HasAttribute("disabled"));
    }

    [Fact]
    public void ClearTheDependentLevels_WhenTheTabChanges_ForOnTabChanged()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model());
        cut.Find("#PlacementTab").Change("Morning");
        cut.Find("#PlacementSection").Change("Waterfront");

        cut.Find("#PlacementTab").Change("Afternoon");

        Assert.Equal(string.Empty, cut.Find("#PlacementSection").GetAttribute("value") ?? string.Empty);
        Assert.Equal("Afternoon", cut.Find(".pb-chip").TextContent.Trim());
    }

    [Fact]
    public void ShowTheRunningCount_WhenPlacedCountIsSet_ForRender()
    {
        IRenderedComponent<PlacementBuilder> cut = RenderBuilder(Model(), placedCount: 2);

        Assert.Equal("2 SO FAR", cut.Find(".pb-count").TextContent.Trim());
    }

    private IRenderedComponent<PlacementBuilder> RenderBuilder(
        PlacementTreeModel model, Action<PlacementPath>? onPlace = null, int placedCount = 0) =>
        Render<PlacementBuilder>(parameters => parameters
            .Add(c => c.Model, model)
            .Add(c => c.PlacedCount, placedCount)
            .Add(c => c.OnPlace, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<PlacementPath>(this, path => onPlace?.Invoke(path))));

    /// <summary>Two Tabs: Morning holds this Activity directly and another at Morning › Waterfront; Afternoon holds one other.</summary>
    private static PlacementTreeModel Model(params PlacementPath[] ghosts)
    {
        Guid morning = Guid.NewGuid(), afternoon = Guid.NewGuid(), waterfront = Guid.NewGuid();
        var server = new PlacementTreeDto
        {
            Tabs = [new TabDto { Id = morning, Name = "Morning", SortOrder = 0 }, new TabDto { Id = afternoon, Name = "Afternoon", SortOrder = 1 }],
            Sections = [new SectionDto { Id = waterfront, ParentTabId = morning, Name = "Waterfront", SortOrder = 0 }],
            Placements =
            [
                new PlacementDto { Id = Guid.NewGuid(), ActivityId = ThisActivity, TabId = morning, SortOrder = 0 },
                new PlacementDto { Id = Guid.NewGuid(), ActivityId = Guid.NewGuid(), TabId = morning, SectionId = waterfront, SortOrder = 0 },
                new PlacementDto { Id = Guid.NewGuid(), ActivityId = Guid.NewGuid(), TabId = afternoon, SortOrder = 0 }
            ]
        };

        return new PlacementTreeModel(server, ThisActivity, "Canoe Basics", new Dictionary<Guid, string>(), ghosts);
    }
}
