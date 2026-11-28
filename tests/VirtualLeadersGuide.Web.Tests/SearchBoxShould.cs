using Bunit;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// The non-zero delays are deliberate: long enough that a following keystroke or click always lands inside the
/// wait it is meant to cancel.
/// </remarks>
public class SearchBoxShould : BunitContext
{
    private readonly List<string> _searches = [];

    public SearchBoxShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    [Fact]
    public void ReportTheTrimmedText_WhenTheUserTypes_ForOnInput()
    {
        IRenderedComponent<SearchBox> cut = RenderSearchBox(TimeSpan.Zero);

        cut.Find("input").Input("  camp  ");

        cut.WaitForAssertion(() => Assert.Equal(["camp"], _searches));
    }

    [Fact]
    public void ReportOnlyTheLastText_WhenTypingContinuesWithinTheDelay_ForOnInput()
    {
        IRenderedComponent<SearchBox> cut = RenderSearchBox(TimeSpan.FromMilliseconds(500));

        cut.Find("input").Input("sp");
        cut.Find("input").Input("spl");

        cut.WaitForAssertion(() => Assert.Equal(["spl"], _searches), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void HideTheClearButton_WhileTheBoxIsEmpty_ForRender()
    {
        IRenderedComponent<SearchBox> cut = RenderSearchBox(TimeSpan.Zero);

        Assert.Empty(cut.FindAll("button[aria-label='Clear search']"));
    }

    [Fact]
    public void ReportAnEmptySearchAtOnceAndEmptyTheBox_WhenClearIsClicked_ForClear()
    {
        IRenderedComponent<SearchBox> cut = RenderSearchBox(TimeSpan.FromMinutes(1));
        cut.Find("input").Input("camp");

        cut.Find("button[aria-label='Clear search']").Click();

        Assert.Equal([""], _searches);
        Assert.Equal("", cut.Find("input").GetAttribute("value") ?? "");
        Assert.Empty(cut.FindAll("button[aria-label='Clear search']"));
    }

    private IRenderedComponent<SearchBox> RenderSearchBox(TimeSpan delay) =>
        Render<SearchBox>(parameters => parameters
            .Add(box => box.AriaLabel, "Search by name")
            .Add(box => box.DebounceDelay, delay)
            .Add(box => box.SearchChanged, (string text) => _searches.Add(text)));
}
