using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.PublicGuide;
using static VirtualLeadersGuide.Web.Tests.GuideBuilder;

namespace VirtualLeadersGuide.Web.Tests;

public class GuideSelectionShould
{
    private static readonly PublicGuideDto TwoTabs = Guide(
        Tab("Day", Screen("Opening"), SubTab("Round Robin", Screen("Rotation"))),
        Tab("Evening", Screen(), SubTab("Campfire", Screen("Skits")), SubTab("Quiet Hours", Screen("Stars"))),
        Tab("Logistics", Screen("Parking")));

    [Fact]
    public void ReturnNull_WhenTheGuideHasNoTabs_ForResolve() =>
        Assert.Null(GuideSelection.Resolve(Guide(), "Day", null));

    [Theory]
    [InlineData(null, null, "Day", null)]
    [InlineData("Evening", "Quiet Hours", "Evening", "Quiet Hours")]
    [InlineData("Day", null, "Day", null)]
    [InlineData("Logistics", null, "Logistics", null)]
    [InlineData("dAY", "round robin", "Day", "Round Robin")]
    public void SelectWhatWasAskedFor_WhenTheGuideHasIt_ForResolve(string? tab, string? sub, string expectedTab, string? expectedSubTab)
    {
        GuideSelection selection = GuideSelection.Resolve(TwoTabs, tab, sub)!;

        Assert.Equal(expectedTab, selection.Tab.Name);
        Assert.Equal(expectedSubTab, selection.SubTab?.Name);
        Assert.False(selection.WasStale);
    }

    [Theory]
    [InlineData("Morning", null, "Day", null)]
    [InlineData("Morning", "Campfire", "Day", null)]
    [InlineData("Day", "Campfire", "Day", null)]
    [InlineData("Evening", "Round Robin", "Evening", "Campfire")]
    [InlineData("Evening", null, "Evening", "Campfire")]
    public void FallBackAndSayItWasStale_WhenTheGuideNoLongerHasIt_ForResolve(
        string tab, string? sub, string expectedTab, string? expectedSubTab)
    {
        GuideSelection selection = GuideSelection.Resolve(TwoTabs, tab, sub)!;

        Assert.Equal(expectedTab, selection.Tab.Name);
        Assert.Equal(expectedSubTab, selection.SubTab?.Name);
        Assert.True(selection.WasStale);
    }

    [Fact]
    public void OpenTheFirstSubTab_WhenTheFirstTabsBareScreenIsEmpty_ForResolve()
    {
        PublicGuideDto guide = Guide(Tab("Evening", Screen(), SubTab("Campfire")));

        GuideSelection selection = GuideSelection.Resolve(guide, null, null)!;

        Assert.Equal("Campfire", selection.SubTab?.Name);
        Assert.False(selection.WasStale);
    }

    [Theory]
    [InlineData("Day", true)]
    [InlineData("Evening", false)]
    [InlineData("Logistics", false)]
    public void ReportWhetherToLeadWithABareEntry_WhenATabIsSelected_ForShowsBareEntry(string tab, bool expected) =>
        Assert.Equal(expected, GuideSelection.Resolve(TwoTabs, tab, null)!.ShowsBareEntry);
}
