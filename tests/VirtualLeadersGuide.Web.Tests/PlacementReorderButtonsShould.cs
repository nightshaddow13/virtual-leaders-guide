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
    [InlineData(1, PlacementDirection.Up, "up")]
    [InlineData(1, PlacementDirection.Down, "down")]
    [InlineData(0, PlacementDirection.Up, "down")]
    [InlineData(2, PlacementDirection.Down, "up")]
    public void FocusTheUsedArrowOrTheOtherAndReportIt_WhenARequestNamesTheRow_ForOnAfterRenderAsync(
        int position, PlacementDirection direction, string focused)
    {
        int reported = 0;
        var leaf = new PlacementLeaf(
            Guid.NewGuid(), "Canoe Basics", IsThisActivity: true, PlacementId, new PlacementPath("Morning"), position)
        {
            Position = position,
            SavedCount = 3
        };

        IRenderedComponent<PlacementReorderButtons> cut = Render<PlacementReorderButtons>(parameters => parameters
            .Add(c => c.Leaf, leaf)
            .Add(c => c.FocusRequest, new PlacementFocusRequest(PlacementId, direction))
            .Add(c => c.OnFocused, EventCallback.Factory.Create(this, () => reported++)));

        Assert.Equal(1, reported);

        JSRuntimeInvocation invocation = Assert.Single(JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
        Assert.IsType<ElementReference>(invocation.Arguments[0])
            .ShouldBeElementReferenceTo(cut.Find($"button[aria-label='Move Morning {focused}']"));
    }
}
