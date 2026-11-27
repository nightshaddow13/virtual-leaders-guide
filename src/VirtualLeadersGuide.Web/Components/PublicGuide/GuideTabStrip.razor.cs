using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.PublicGuide;

namespace VirtualLeadersGuide.Web.Components.PublicGuide;

/// <summary>The public guide's Tab strip and, under it, the selected Tab's Sub Tab strip (wireframes 3a, 3c).</summary>
/// <remarks>
/// Real links, since the page has no circuit (ADR-0080). The two strips are sibling roots with no wrapper,
/// because the sticky Tab strip has to share a containing block with the content it sticks over. Both turn off
/// enhanced navigation, which keeps the scroll position when only the query string changes: a new screen has
/// to start at the top.
/// </remarks>
public partial class GuideTabStrip
{
    /// <summary>The Event's Slug, for building links.</summary>
    [Parameter, EditorRequired]
    public string Slug { get; set; } = "";

    /// <summary>The Event's guide.</summary>
    [Parameter, EditorRequired]
    public PublicGuideDto Guide { get; set; } = default!;

    /// <summary>The screen currently shown.</summary>
    [Parameter, EditorRequired]
    public GuideSelection Selection { get; set; } = default!;

    private string Href(PublicGuideTabDto tab, PublicGuideSubTabDto? subTab)
    {
        string href = $"e/{Uri.EscapeDataString(Slug)}?{GuideSelection.TabParameter}={Uri.EscapeDataString(tab.Name)}";
        return subTab is null ? href : $"{href}&{GuideSelection.SubTabParameter}={Uri.EscapeDataString(subTab.Name)}";
    }

    private static string? Current(bool isSelected) => isSelected ? "page" : null;
}
