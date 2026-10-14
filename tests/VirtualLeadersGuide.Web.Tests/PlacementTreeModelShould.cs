using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Pure-logic coverage of <see cref="PlacementTreeModel"/> (P5-11, #96; plan decision 4): the merged
/// server-plus-ghost tree behind the Activity page's live tree, autofill choices, canonical casing, near-
/// duplicate nudge and duplicate detection. No Blazor, no HTTP - the component tests in
/// <see cref="ActivityPlacementBuilderShould"/> only need to prove the UI is wired to these answers.
/// </remarks>
public class PlacementTreeModelShould
{
    private static readonly Guid ThisActivity = Guid.NewGuid();
    private static readonly Guid OtherActivity = Guid.NewGuid();

    private readonly Guid _morning = Guid.NewGuid();
    private readonly Guid _afternoon = Guid.NewGuid();
    private readonly Guid _roundRobin = Guid.NewGuid();
    private readonly Guid _waterfront = Guid.NewGuid();
    private readonly Guid _canoeing = Guid.NewGuid();
    private readonly Guid _afternoonWaterfront = Guid.NewGuid();

    /// <summary>Morning (order 0) › Round Robin › Waterfront › Canoeing holds "Other"; Afternoon (order 1) › Waterfront (a Section directly under the bare Tab) holds "This".</summary>
    private PlacementTreeDto ServerTree() => new()
    {
        Tabs = [new TabDto { Id = _afternoon, Name = "Afternoon", SortOrder = 1 }, new TabDto { Id = _morning, Name = "Morning", SortOrder = 0 }],
        SubTabs = [new SubTabDto { Id = _roundRobin, TabId = _morning, Name = "Round Robin", SortOrder = 0 }],
        Sections =
        [
            new SectionDto { Id = _waterfront, ParentSubTabId = _roundRobin, Name = "Waterfront", SortOrder = 0 },
            new SectionDto { Id = _afternoonWaterfront, ParentTabId = _afternoon, Name = "Waterfront", SortOrder = 0 }
        ],
        SubSections = [new SubSectionDto { Id = _canoeing, SectionId = _waterfront, Name = "Canoeing", SortOrder = 0 }],
        Placements =
        [
            new PlacementDto { Id = Guid.NewGuid(), ActivityId = OtherActivity, TabId = _morning, SubTabId = _roundRobin, SectionId = _waterfront, SubSectionId = _canoeing, SortOrder = 0 },
            new PlacementDto { Id = Guid.NewGuid(), ActivityId = ThisActivity, TabId = _afternoon, SectionId = _afternoonWaterfront, SortOrder = 0 }
        ]
    };

    private PlacementTreeModel Model(params PlacementPath[] ghosts) => new(
        ServerTree(), ThisActivity, "Canoe Basics",
        new Dictionary<Guid, string> { [OtherActivity] = "Swamped Canoe Rescue" }, ghosts);

    [Fact]
    public void OrderTabsBySortOrder_WhenTheTreeIsBuilt_ForRoots()
    {
        Assert.Equal(["Morning", "Afternoon"], Model().Roots.Select(r => r.Name));
    }

    [Fact]
    public void PlaceEachActivityAtTheDeepestTierOfItsPath_WhenTheTreeIsBuilt_ForRoots()
    {
        TierNode morning = Model().Roots[0];

        TierNode canoeing = morning.Children.Single().Children.Single().Children.Single();

        Assert.Equal(TierKind.SubSection, canoeing.Kind);
        PlacementLeaf leaf = Assert.Single(canoeing.Leaves);
        Assert.Equal("Swamped Canoe Rescue", leaf.ActivityName);
        Assert.False(leaf.IsThisActivity);
    }

    [Fact]
    public void LightThisActivitysRows_WhenTheTreeIsBuilt_ForRoots()
    {
        TierNode afternoon = Model().Roots[1].Children.Single();

        PlacementLeaf leaf = Assert.Single(afternoon.Leaves);

        Assert.True(leaf.IsThisActivity);
        Assert.False(leaf.IsPending);
        Assert.Equal("Canoe Basics", leaf.ActivityName);
    }

    [Fact]
    public void ListOnlyTheChosenTabsSubTabs_WhenASubTabIsRequested_ForSubTabOptions()
    {
        PlacementTreeModel model = Model();

        Assert.Equal(["Round Robin"], model.SubTabOptions("Morning").Select(o => o.Name));
        Assert.Empty(model.SubTabOptions("Afternoon"));
        Assert.Empty(model.SubTabOptions("Evening"));
    }

    /// <remarks>Each level lists only children of the level above (wireframe 2b): Afternoon's bare-tab Waterfront isn't offered under Morning's Round Robin, and vice versa.</remarks>
    [Fact]
    public void ScopeSectionsToTheirImmediateParent_WhenSectionsAreRequested_ForSectionOptions()
    {
        PlacementTreeModel model = Model();

        Assert.Equal(["Waterfront"], model.SectionOptions("Morning", "Round Robin").Select(o => o.Name));
        Assert.Empty(model.SectionOptions("Morning", null));
        Assert.Equal(["Waterfront"], model.SectionOptions("Afternoon", null).Select(o => o.Name));
    }

    [Fact]
    public void ListSubSectionsOfTheChosenSectionOnly_WhenSubSectionsAreRequested_ForSubSectionOptions()
    {
        PlacementTreeModel model = Model();

        Assert.Equal(["Canoeing"], model.SubSectionOptions("Morning", "Round Robin", "Waterfront").Select(o => o.Name));
        Assert.Empty(model.SubSectionOptions("Afternoon", null, "Waterfront"));
    }

    [Fact]
    public void CountDistinctActivitiesAtOrUnderEachTier_WhenOptionsAreListed_ForTabOptions()
    {
        PlacementTreeModel model = Model();

        Assert.Equal(1, model.TabOptions().Single(o => o.Name == "Morning").ActivityCount);
        Assert.Equal(1, model.TabOptions().Single(o => o.Name == "Afternoon").ActivityCount);
    }

    [Fact]
    public void RewriteEachSegmentToTheExistingCasing_WhenTheTypedNamesMatchCaseInsensitively_ForCanonicalize()
    {
        PlacementTreeModel model = Model();

        PlacementPath canonical = model.Canonicalize(new PlacementPath("morning", "ROUND robin", "waterfront", "CANOEING"));

        Assert.Equal("Morning › Round Robin › Waterfront › Canoeing", canonical.Display);
    }

    [Fact]
    public void KeepTypedCasingFromTheFirstUnmatchedSegmentOn_WhenTheChainDiverges_ForCanonicalize()
    {
        PlacementTreeModel model = Model();

        PlacementPath canonical = model.Canonicalize(new PlacementPath("morning", "new sub tab", "waterfront"));

        Assert.Equal("Morning › new sub tab › waterfront", canonical.Display);
    }

    [Fact]
    public void FlagAnExactRepeatOfTheServersPath_WhenThisActivityIsAlreadyPlacedThere_ForIsAlreadyPlaced()
    {
        PlacementTreeModel model = Model();

        Assert.True(model.IsAlreadyPlaced(new PlacementPath("afternoon", null, "WATERFRONT")));
    }

    [Fact]
    public void NotFlagADifferentPathOrAnotherActivitysPath_WhenCheckingForDuplicates_ForIsAlreadyPlaced()
    {
        PlacementTreeModel model = Model();

        Assert.False(model.IsAlreadyPlaced(new PlacementPath("Afternoon", "Round Robin")));
        Assert.False(model.IsAlreadyPlaced(new PlacementPath("Morning", "Round Robin", "Waterfront", "Canoeing")));
    }

    // --- decision 4: ghost rows count as existing -------------------------------------------------------

    [Fact]
    public void OfferAGhostIntroducedTabAsAnExistingChoice_WhenAPendingRowCreatesIt_ForTabOptions()
    {
        PlacementTreeModel model = Model(new PlacementPath("Evening"));

        TierOption evening = model.TabOptions().Single(o => o.Name == "Evening");

        Assert.Equal(1, evening.ActivityCount);
    }

    [Fact]
    public void ListGhostIntroducedTiersUnderTheirParents_WhenAPendingRowCreatesThem_ForSectionOptions()
    {
        PlacementTreeModel model = Model(new PlacementPath("Evening", "Campfire", "Songs"));

        Assert.Equal(["Campfire"], model.SubTabOptions("Evening").Select(o => o.Name));
        Assert.Equal(["Songs"], model.SectionOptions("Evening", "Campfire").Select(o => o.Name));
    }

    [Fact]
    public void FlagARepeatOfAGhostsPath_WhenThePendingRowAlreadyHoldsIt_ForIsAlreadyPlaced()
    {
        PlacementTreeModel model = Model(new PlacementPath("Evening", "Campfire"));

        Assert.True(model.IsAlreadyPlaced(new PlacementPath("EVENING", "campfire")));
        Assert.False(model.IsAlreadyPlaced(new PlacementPath("Evening")));
    }

    [Fact]
    public void CanonicalizeAgainstAGhostIntroducedTier_WhenALaterRowReusesItWithDifferentCasing_ForCanonicalize()
    {
        PlacementTreeModel model = Model(new PlacementPath("Evening"));

        Assert.Equal("Evening", model.Canonicalize(new PlacementPath("evening")).Tab);
    }

    [Fact]
    public void DrawGhostRowsPendingAndMarkOnlyGhostCreatedTiersPending_WhenGhostsAreAdded_ForRoots()
    {
        PlacementTreeModel model = Model(new PlacementPath("Morning", "Round Robin", "Rapids"), new PlacementPath("Evening"));

        TierNode morning = model.Roots.Single(r => r.Name == "Morning");
        TierNode rapids = morning.Children.Single().Children.Single(c => c.Name == "Rapids");
        TierNode evening = model.Roots.Single(r => r.Name == "Evening");

        Assert.False(morning.IsPending);
        Assert.True(rapids.IsPending);
        Assert.True(evening.IsPending);
        Assert.True(Assert.Single(rapids.Leaves).IsPending);
    }

    [Fact]
    public void SortGhostOnlyTabsAfterServerTabs_WhenGhostsAreAdded_ForRoots()
    {
        PlacementTreeModel model = Model(new PlacementPath("Evening"));

        Assert.Equal(["Morning", "Afternoon", "Evening"], model.Roots.Select(r => r.Name));
    }

    // --- NEW badges --------------------------------------------------------------------------------------

    [Fact]
    public void MarkOnlyTheTiersSavingWouldCreate_WhenThePathExtendsAnExistingOne_ForDescribe()
    {
        PlacementTreeModel model = Model();

        IReadOnlyList<PathLevel> levels = model.Describe(new PlacementPath("morning", "Round Robin", "Rapids"));

        Assert.Equal([("Morning", false), ("Round Robin", false), ("Rapids", true)], levels.Select(l => (l.Name, l.IsNew)));
    }

    [Fact]
    public void NotMarkAGhostIntroducedTierNew_WhenAnEarlierPendingRowAlreadyCreatesIt_ForDescribe()
    {
        PlacementTreeModel model = Model(new PlacementPath("Evening"));

        Assert.All(model.Describe(new PlacementPath("evening")), level => Assert.False(level.IsNew));
    }

    [Fact]
    public void MarkEveryLevelNew_WhenNothingOnThePathExists_ForDescribe()
    {
        PlacementTreeModel model = Model();

        IReadOnlyList<PathLevel> levels = model.Describe(new PlacementPath("Evening", "Campfire", "Songs", "Rounds"));

        Assert.Equal(4, levels.Count);
        Assert.All(levels, level => Assert.True(level.IsNew));
    }

    // --- did you mean ------------------------------------------------------------------------------------

    [Fact]
    public void SuggestTheExistingSpelling_WhenTheTypedNameDiffersOnlyByCase_ForNearDuplicateOf()
    {
        IReadOnlyList<TierOption> options = Model().SectionOptions("Afternoon", null);

        Assert.Equal("Waterfront", PlacementTreeModel.NearDuplicateOf(options, "waterfront"));
    }

    [Fact]
    public void SuggestNothing_WhenTheTypedNameMatchesExactlyIsNewOrIsBlank_ForNearDuplicateOf()
    {
        IReadOnlyList<TierOption> options = Model().SectionOptions("Afternoon", null);

        Assert.Null(PlacementTreeModel.NearDuplicateOf(options, "Waterfront"));
        Assert.Null(PlacementTreeModel.NearDuplicateOf(options, "Boating"));
        Assert.Null(PlacementTreeModel.NearDuplicateOf(options, "  "));
        Assert.Null(PlacementTreeModel.NearDuplicateOf(options, null));
    }

    // --- PlacementPath -----------------------------------------------------------------------------------

    [Fact]
    public void TreatPathsAsEqualIgnoringCaseAndPadding_WhenComparingTwoPaths_ForEquals()
    {
        var left = new PlacementPath(" Morning ", "round robin");
        var right = new PlacementPath("MORNING", "Round Robin");

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, new PlacementPath("Morning"));
    }

    [Fact]
    public void RejectASubSectionWithNoSection_WhenAPathIsBuilt_ForConstructor()
    {
        Assert.Throws<ArgumentException>(() => new PlacementPath("Morning", subSection: "Canoeing"));
    }

    [Fact]
    public void RejectABlankTab_WhenAPathIsBuilt_ForConstructor()
    {
        Assert.Throws<ArgumentException>(() => new PlacementPath("   "));
    }

    [Fact]
    public void JoinSetSegmentsWithAChevron_WhenDisplayed_ForDisplay()
    {
        Assert.Equal("Morning › Waterfront", new PlacementPath("Morning", null, "Waterfront").Display);
    }
}
