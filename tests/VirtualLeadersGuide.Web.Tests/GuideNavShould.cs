using AngleSharp.Dom;
using Bunit;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Components.PublicGuide;
using VirtualLeadersGuide.Web.PublicGuide;
using static VirtualLeadersGuide.Web.Tests.GuideBuilder;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>The wide-screen entries are under the "Tabs" nav and the narrow-screen menu under "Guide menu"; both are always rendered, CSS picks one.</remarks>
public class GuideNavShould : BunitContext
{
    private const string Wide = "nav[aria-label='Tabs']";
    private const string Narrow = "nav[aria-label='Guide menu']";

    private static readonly PublicGuideDto SampleGuide = Guide(
        Tab("Day", Screen("Opening"), SubTab("Round Robin"), SubTab("Cub & Webelos")),
        Tab("Evening", Screen(), SubTab("Campfire")),
        Tab("Logistics", Screen("Parking")));

    public GuideNavShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void GiveATabADropdownOnlyWhenItHasSubTabs_WhenRendered_ForRender()
    {
        IRenderedComponent<GuideNav> cut = RenderNav("Day", null);

        Assert.Equal(["Day", "Evening"], cut.FindAll($"{Wide} summary").Select(Text));
        Assert.Equal("Logistics", Text(Assert.Single(cut.FindAll($"{Wide} > a"))));
    }

    [Fact]
    public void LeadADropdownWithTheTabsOwnName_WhenItsBareScreenHasContent_ForRender()
    {
        IRenderedComponent<GuideNav> cut = RenderNav("Day", null);

        Assert.Equal(["Day", "Round Robin", "Cub & Webelos", "Campfire"], cut.FindAll($"{Wide} details a").Select(Text));
    }

    [Fact]
    public void MarkOnlyTheScreenShown_WhenASubTabIsSelected_ForRender()
    {
        IRenderedComponent<GuideNav> cut = RenderNav("Day", "Round Robin");

        Assert.Equal("Round Robin", Text(Assert.Single(cut.FindAll($"{Wide} a[aria-current]"))));
        Assert.Contains("rz-variant-flat", cut.FindAll($"{Wide} summary")[0].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void ListEveryScreen_WhenFoldedIntoOneMenu_ForRender()
    {
        IRenderedComponent<GuideNav> cut = RenderNav("Day", null);

        Assert.Equal(
            ["Day", "Round Robin", "Cub & Webelos", "Campfire", "Logistics"],
            cut.FindAll($"{Narrow} a").Select(Text));
    }

    [Fact]
    public void LinkEachEntryToItsScreenByEscapedName_WhenRendered_ForRender()
    {
        IRenderedComponent<GuideNav> cut = RenderNav("Day", null);

        Assert.Equal(
            [
                "e/summer-camporee?tab=Day",
                "e/summer-camporee?tab=Day&sub=Round%20Robin",
                "e/summer-camporee?tab=Day&sub=Cub%20%26%20Webelos",
                "e/summer-camporee?tab=Evening&sub=Campfire",
                "e/summer-camporee?tab=Logistics"
            ],
            cut.FindAll($"{Narrow} a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void PutTabsThatDoNotFitUnderMore_WhenThereAreMany_ForRender()
    {
        PublicGuideDto guide = Guide(
            Tab("Day"), Tab("Packing and Gear"), Tab("Saturday Program"), Tab("Maps and Directions"), Tab("Health and Safety"));

        IRenderedComponent<GuideNav> cut = Render<GuideNav>(parameters => parameters
            .Add(c => c.Slug, "summer-camporee")
            .Add(c => c.Guide, guide)
            .Add(c => c.Selection, GuideSelection.Resolve(guide, "Maps and Directions", null)!));

        Assert.Equal(
            ["Day", "Packing and Gear", "Saturday Program"], cut.FindAll($"{Wide} > a").Select(Text));
        Assert.Equal("More", Text(Assert.Single(cut.FindAll($"{Wide} summary"))));
        Assert.Contains("rz-variant-flat", cut.Find($"{Wide} summary").ClassName, StringComparison.Ordinal);
        Assert.Equal(["Maps and Directions", "Health and Safety"], cut.FindAll($"{Wide} details a").Select(Text));
        Assert.Equal(5, cut.FindAll($"{Narrow} a").Count);
    }

    private static string Text(IElement element) => element.TextContent.Replace("arrow_drop_down", "").Trim();

    private IRenderedComponent<GuideNav> RenderNav(string tab, string? sub) =>
        Render<GuideNav>(parameters => parameters
            .Add(c => c.Slug, "summer-camporee")
            .Add(c => c.Guide, SampleGuide)
            .Add(c => c.Selection, GuideSelection.Resolve(SampleGuide, tab, sub)!));
}
