using Bunit;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Components.PublicGuide;
using VirtualLeadersGuide.Web.PublicGuide;
using static VirtualLeadersGuide.Web.Tests.GuideBuilder;

namespace VirtualLeadersGuide.Web.Tests;

public class GuideTabStripShould : BunitContext
{
    private static readonly PublicGuideDto SampleGuide = Guide(
        Tab("Day", Screen("Opening"), SubTab("Round Robin"), SubTab("Cub & Webelos")),
        Tab("Evening", Screen(), SubTab("Campfire")),
        Tab("Logistics", Screen("Parking")));

    [Fact]
    public void MarkTheSelectedTabAndSubTab_WhenBothAreSelected_ForRender()
    {
        IRenderedComponent<GuideTabStrip> cut = RenderStrip("Day", "Round Robin");

        Assert.Equal(["Day", "Round Robin"], cut.FindAll("a[aria-current]").Select(a => a.TextContent));
    }

    [Fact]
    public void LeadTheSubTabStripWithTheTabsOwnName_WhenItsBareScreenHasContent_ForRender()
    {
        IRenderedComponent<GuideTabStrip> cut = RenderStrip("Day", null);

        Assert.Equal(["Day", "Round Robin", "Cub & Webelos"], cut.FindAll(".vlg-subtab").Select(a => a.TextContent));
        Assert.Equal("Day", cut.Find(".vlg-subtab[aria-current]").TextContent);
    }

    [Fact]
    public void OmitTheBareEntry_WhenTheBareScreenIsEmpty_ForRender()
    {
        IRenderedComponent<GuideTabStrip> cut = RenderStrip("Evening", null);

        Assert.Equal("Campfire", Assert.Single(cut.FindAll(".vlg-subtab")).TextContent);
    }

    [Fact]
    public void ShowNoSubTabStrip_WhenTheTabHasNoSubTabs_ForRender()
    {
        IRenderedComponent<GuideTabStrip> cut = RenderStrip("Logistics", null);

        Assert.Empty(cut.FindAll(".vlg-subtabs"));
    }

    [Fact]
    public void LinkEachEntryToItsScreenByEscapedName_WhenRendered_ForRender()
    {
        IRenderedComponent<GuideTabStrip> cut = RenderStrip("Day", null);

        Assert.Equal(
            ["e/summer-camporee?tab=Day", "e/summer-camporee?tab=Evening&sub=Campfire", "e/summer-camporee?tab=Logistics"],
            cut.FindAll(".vlg-tab").Select(a => a.GetAttribute("href")));
        Assert.Equal(
            "e/summer-camporee?tab=Day&sub=Cub%20%26%20Webelos",
            cut.FindAll(".vlg-subtab")[2].GetAttribute("href"));
    }

    private IRenderedComponent<GuideTabStrip> RenderStrip(string tab, string? sub) =>
        Render<GuideTabStrip>(parameters => parameters
            .Add(c => c.Slug, "summer-camporee")
            .Add(c => c.Guide, SampleGuide)
            .Add(c => c.Selection, GuideSelection.Resolve(SampleGuide, tab, sub)!));
}
