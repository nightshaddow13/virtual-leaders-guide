using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Tests;

public class GuideLinkButtonShould : BunitContext
{
    public GuideLinkButtonShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    [Theory]
    [InlineData(EventStatus.Live)]
    [InlineData(EventStatus.Past)]
    [InlineData(EventStatus.Cancelled)]
    public void RenderAnAnchorOpeningTheGuideInANewTab_WhenTheEventIsNotADraft(EventStatus status)
    {
        IRenderedComponent<GuideLinkButton> cut = RenderButton(status, isAdmin: true);

        IElement link = cut.Find("a");

        Assert.Equal("e/fall-camporee", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener", link.GetAttribute("rel"));
        Assert.Equal("Open guide", link.GetAttribute("aria-label"));
    }

    [Fact]
    public void RenderADisabledButtonAndNoAnchor_WhenTheEventIsDraft()
    {
        IRenderedComponent<GuideLinkButton> cut = RenderButton(EventStatus.Draft, isAdmin: true);

        Assert.Empty(cut.FindAll("a"));
        Assert.True(cut.Find("button[aria-label='Open guide']").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(true, GuideLinkButton.AdminDraftTooltipText)]
    [InlineData(false, GuideLinkButton.DirectorDraftTooltipText)]
    public void OpenTheViewersOwnDraftTooltip_WhenADraftEventsButtonIsHovered(bool isAdmin, string expected)
    {
        string? opened = null;
        Services.GetRequiredService<TooltipService>().OnOpen += (_, _, options) => opened = options.Text;
        IRenderedComponent<GuideLinkButton> cut = RenderButton(EventStatus.Draft, isAdmin);

        cut.Find("span").TriggerEvent("onmouseenter", new MouseEventArgs());

        Assert.Equal(expected, opened);
    }

    private IRenderedComponent<GuideLinkButton> RenderButton(EventStatus status, bool isAdmin) =>
        Render<GuideLinkButton>(parameters => parameters
            .Add(button => button.Slug, "fall-camporee")
            .Add(button => button.Status, status)
            .Add(button => button.IsAdmin, isAdmin));
}
