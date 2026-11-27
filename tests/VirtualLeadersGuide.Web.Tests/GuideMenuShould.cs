using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.PublicGuide;
using static VirtualLeadersGuide.Web.Tests.GuideBuilder;

namespace VirtualLeadersGuide.Web.Tests;

public class GuideMenuShould
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(20, 7)]
    public void KeepAsManyTabsAsFit_WhenCounted_ForShownCount(int tabs, int expected) =>
        Assert.Equal(expected, GuideMenu.ShownCount(Guide([.. Enumerable.Range(0, tabs).Select(_ => Tab("Day"))])));

    [Fact]
    public void ShowTheFirstTab_WhenEvenItIsTooLong_ForShownCount() =>
        Assert.Equal(1, GuideMenu.ShownCount(Guide(Tab(new string('x', 90)), Tab("Day"))));

    [Fact]
    public void LeadWithTheTabsOwnName_WhenItsBareScreenHasContent_ForScreens()
    {
        PublicGuideTabDto tab = Tab("Day", Screen("Opening"), SubTab("Round Robin"));

        Assert.Equal(["Day", "Round Robin"], GuideMenu.Screens(tab).Select(s => s.Name));
    }

    [Fact]
    public void OfferOnlyTheSubTabs_WhenTheBareScreenIsEmpty_ForScreens()
    {
        PublicGuideTabDto tab = Tab("Evening", Screen(), SubTab("Campfire"));

        Assert.Equal(["Campfire"], GuideMenu.Screens(tab).Select(s => s.Name));
    }
}
