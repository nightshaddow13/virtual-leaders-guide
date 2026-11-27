using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Components.PublicGuide;
using VirtualLeadersGuide.Web.Markdown;
using static VirtualLeadersGuide.Web.Tests.GuideBuilder;

namespace VirtualLeadersGuide.Web.Tests;

public class GuideScreenShould : BunitContext
{
    public GuideScreenShould() => Services.AddSingleton(new MarkdownRenderer());

    [Fact]
    public void ShowTheTitleAndRenderedContent_WhenTheScreenHoldsAnInfoPage_ForRender()
    {
        IRenderedComponent<GuideScreen> cut = RenderScreen(InfoPageScreen("About", "Welcome to **camp**!"));

        Assert.Equal("About", cut.Find("h2").TextContent);
        Assert.Equal("camp", cut.Find("strong").TextContent);
        Assert.Empty(cut.FindAll("article"));
    }

    [Fact]
    public void ShowDirectActivitiesThenSectionsThenSubSections_WhenTheScreenHoldsActivities_ForRender()
    {
        var screen = new PublicGuideScreenDto
        {
            Activities = [Activity("Opening", "Bring a **hat**.")],
            Sections =
            [
                new PublicGuideSectionDto
                {
                    Name = "Shooting Sports (Ranges)",
                    Activities = [Activity("Safety Talk")],
                    SubSections = [new PublicGuideSubSectionDto { Name = "Archery", Activities = [Activity("Far Butts")] }]
                }
            ]
        };

        IRenderedComponent<GuideScreen> cut = RenderScreen(screen);

        Assert.Equal(
            ["Opening", "Shooting Sports (Ranges)", "Safety Talk", "Archery", "Far Butts"],
            cut.FindAll("h2, h3, h4").Select(h => h.TextContent));
        Assert.Equal("Far Butts", cut.Find("h4").TextContent);
        Assert.Equal("hat", cut.Find("strong").TextContent);
    }

    [Fact]
    public void ShowTheEmptyState_WhenNothingIsPlaced_ForRender()
    {
        IRenderedComponent<GuideScreen> cut = RenderScreen(Screen());

        Assert.Contains("added here yet", cut.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<GuideScreen> RenderScreen(PublicGuideScreenDto screen) =>
        Render<GuideScreen>(parameters => parameters.Add(c => c.Screen, screen));
}
