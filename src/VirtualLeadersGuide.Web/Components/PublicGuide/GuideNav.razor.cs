using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.PublicGuide;

namespace VirtualLeadersGuide.Web.Components.PublicGuide;

/// <summary>
/// A guide's Tabs as they sit in the top bar: a link per Tab, or a dropdown for a Tab with Sub Tabs, with
/// the ones that don't fit under More; one menu button on a narrow screen (P2-26, #241).
/// </summary>
/// <remarks>
/// Real links inside <c>&lt;details&gt;</c>, since the page has no circuit (ADR-0080). Enhanced navigation is
/// off: it keeps the scroll position and an open menu when only the query string changes.
/// </remarks>
public partial class GuideNav
{
    /// <summary>The section the top bar shows this in.</summary>
    public const string HeaderSection = "guide-nav";

    /// <summary>The Event's Slug, for building links.</summary>
    [Parameter, EditorRequired]
    public string Slug { get; set; } = "";

    /// <summary>The Event's guide.</summary>
    [Parameter, EditorRequired]
    public PublicGuideDto Guide { get; set; } = default!;

    /// <summary>The screen currently shown.</summary>
    [Parameter, EditorRequired]
    public GuideSelection Selection { get; set; } = default!;

    private IReadOnlyList<PublicGuideTabDto> Overflow => [.. Guide.Tabs.Skip(GuideMenu.ShownCount(Guide))];
}
