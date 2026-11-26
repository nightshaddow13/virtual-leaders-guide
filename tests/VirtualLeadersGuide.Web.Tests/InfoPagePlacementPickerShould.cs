using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.InfoPages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>Pure-logic coverage of <see cref="InfoPagePlacementPicker"/> (P5-24, #208).</remarks>
public class InfoPagePlacementPickerShould
{
    private static readonly Guid ThisPage = Guid.NewGuid();
    private static readonly Guid OtherPage = Guid.NewGuid();

    private readonly Guid _logistics = Guid.NewGuid();
    private readonly Guid _arrival = Guid.NewGuid();
    private readonly Guid _morning = Guid.NewGuid();
    private readonly Guid _about = Guid.NewGuid();

    /// <summary>"Logistics" holds another InfoPage, "Logistics › Arrival" holds this one, "Morning" shows Activities, "About" is empty.</summary>
    private InfoPagePlacementPicker Picker() => new(
        new PlacementTreeDto
        {
            Tabs =
            [
                new TabDto { Id = _logistics, Name = "Logistics", SortOrder = 0 },
                new TabDto { Id = _morning, Name = "Morning", SortOrder = 1 },
                new TabDto { Id = _about, Name = "About", SortOrder = 2 }
            ],
            SubTabs = [new SubTabDto { Id = _arrival, TabId = _logistics, Name = "Arrival", SortOrder = 0 }],
            Placements = [new PlacementDto { Id = Guid.NewGuid(), ActivityId = Guid.NewGuid(), TabId = _morning, SortOrder = 0 }],
            InfoPagePlacements =
            [
                new InfoPagePlacementDto { Id = Guid.NewGuid(), InfoPageId = OtherPage, TabId = _logistics },
                new InfoPagePlacementDto { Id = Guid.NewGuid(), InfoPageId = ThisPage, TabId = _logistics, SubTabId = _arrival }
            ]
        },
        ThisPage,
        new Dictionary<Guid, string> { [OtherPage] = "Packing List" });

    [Fact]
    public void OfferEveryTab_IncludingOnesWithNoActivities_ForTabOptions()
    {
        Assert.Equal(["Logistics", "Morning", "About"], Picker().TabOptions().Select(o => o.Name));
    }

    [Fact]
    public void DescribeWhereThisInfoPageIs_ForCurrent()
    {
        Assert.Equal("Logistics › Arrival", Picker().Current!.Display);
    }

    [Theory]
    [InlineData("Morning", null, "shows Activities")]
    [InlineData("logistics", null, "shows Packing List")]
    [InlineData("Logistics", "Arrival", "")]
    [InlineData("About", null, "")]
    [InlineData("Brand New", null, "")]
    [InlineData("Logistics", "Brand New", "")]
    public void SayWhatFillsTheExactScreen_ForAnnotate(string tab, string? subTab, string expected)
    {
        Assert.Equal(expected, Picker().Annotate(tab, subTab));
    }
}
