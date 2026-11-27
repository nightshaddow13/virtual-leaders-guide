using Bunit;
using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Web.Activities;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

public class PlacementReorderButtonsShould : BunitContext
{
    private static readonly Guid PlacementId = Guid.NewGuid();

    public PlacementReorderButtonsShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Theory]
    [InlineData(1, -1, "up")]
    [InlineData(1, 1, "down")]
    [InlineData(0, -1, "down")]
    [InlineData(2, 1, "up")]
    public void FocusTheUsedArrowOrTheOtherWhenItIsDisabled_WhenARequestNamesTheRow_ForFocusRequest(
        int position, int delta, string focused)
    {
        var leaf = new PlacementLeaf(
            Guid.NewGuid(), "Canoe Basics", IsThisActivity: true, PlacementId, new PlacementPath("Morning"), position)
        {
            Position = position,
            SavedCount = 3
        };

        IRenderedComponent<PlacementReorderButtons> cut = Render<PlacementReorderButtons>(parameters => parameters
            .Add(c => c.Leaf, leaf)
            .Add(c => c.FocusRequest, new PlacementFocusRequest(PlacementId, delta)));

        JSRuntimeInvocation invocation = Assert.Single(JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
        Assert.IsType<ElementReference>(invocation.Arguments[0])
            .ShouldBeElementReferenceTo(cut.Find($"button[aria-label='Move Morning {focused}']"));
    }
}
