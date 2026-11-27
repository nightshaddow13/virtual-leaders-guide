using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

public class DisabledTooltipShould : BunitContext
{
    private const string Reason = "Not available yet.";

    public DisabledTooltipShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    [Theory]
    [InlineData("onmouseenter")]
    [InlineData("onfocus")]
    public void OpenTheTooltip_WhenTheWrapperIsHoveredOrFocused_ForShow(string trigger)
    {
        string? opened = null;
        Services.GetRequiredService<TooltipService>().OnOpen += (_, _, options) => opened = options.Text;
        IElement wrapper = RenderWrapper().Find("[role=group]");

        if (trigger == "onfocus")
        {
            wrapper.Focus();
        }
        else
        {
            wrapper.TriggerEvent(trigger, new MouseEventArgs());
        }

        Assert.Equal(Reason, opened);
    }

    [Fact]
    public void CloseTheTooltip_WhenTheWrapperLosesFocus_ForHide()
    {
        bool closed = false;
        Services.GetRequiredService<TooltipService>().OnClose += () => closed = true;

        RenderWrapper().Find("[role=group]").Blur();

        Assert.True(closed);
    }

    [Fact]
    public void BeFocusableAndNamedByTheReason_WhenRendered_ForBuildRenderTree()
    {
        IRenderedComponent<DisabledTooltip> cut = RenderWrapper();

        IElement wrapper = cut.Find("[role=group]");

        Assert.Equal("0", wrapper.GetAttribute("tabindex"));
        Assert.Equal(Reason, cut.Find($"#{wrapper.GetAttribute("aria-labelledby")}").TextContent);
    }

    private IRenderedComponent<DisabledTooltip> RenderWrapper() =>
        Render<DisabledTooltip>(parameters => parameters
            .Add(tooltip => tooltip.Text, Reason)
            .AddChildContent("<button disabled>Delete</button>"));
}
