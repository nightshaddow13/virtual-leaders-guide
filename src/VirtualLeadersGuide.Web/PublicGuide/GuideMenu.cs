using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.PublicGuide;

/// <summary>How a guide's Tabs are laid out as menu entries in the top bar (ADR-0080).</summary>
public static class GuideMenu
{
    /// <summary>Roughly how many characters the bar holds on one row at its narrowest wide layout (1024px).</summary>
    private const int LabelBudget = 52;

    /// <summary>What an entry's side padding costs, in characters.</summary>
    private const int PaddingCost = 4;

    /// <summary>What a dropdown's arrow costs, in characters.</summary>
    private const int ArrowCost = 2;

    /// <summary>A Tab's screens: its bare screen when that has content, under the Tab's own name, then its Sub Tabs.</summary>
    /// <param name="tab">The Tab.</param>
    /// <returns>Each screen's label and Sub Tab, <see langword="null"/> for the bare screen.</returns>
    public static IEnumerable<(string Name, PublicGuideSubTabDto? SubTab)> Screens(PublicGuideTabDto tab)
    {
        if (!tab.Screen.IsEmpty)
        {
            yield return (tab.Name, null);
        }

        foreach (PublicGuideSubTabDto subTab in tab.SubTabs)
        {
            yield return (subTab.Name, subTab);
        }
    }

    /// <summary>How many of the guide's Tabs get their own place in the bar; the rest go under More.</summary>
    /// <param name="guide">The Event's guide.</param>
    /// <returns>At least one, and every Tab when they all fit.</returns>
    /// <remarks>
    /// Counted from label lengths, not measured: the page is rendered on the server with no circuit, so it
    /// cannot know widths.
    /// </remarks>
    public static int ShownCount(PublicGuideDto guide)
    {
        int used = 0;
        int shown = 0;

        foreach (PublicGuideTabDto tab in guide.Tabs)
        {
            used += tab.Name.Length + PaddingCost + (tab.SubTabs.Count > 0 ? ArrowCost : 0);
            if (shown > 0 && used > LabelBudget)
            {
                break;
            }

            shown++;
        }

        return shown;
    }

    /// <summary>The link to one screen.</summary>
    /// <param name="slug">The Event's Slug.</param>
    /// <param name="tab">The Tab.</param>
    /// <param name="subTab">The Sub Tab, or <see langword="null"/> for the bare Tab.</param>
    /// <returns>A relative address with both names escaped.</returns>
    public static string Href(string slug, PublicGuideTabDto tab, PublicGuideSubTabDto? subTab)
    {
        string href = $"e/{Uri.EscapeDataString(slug)}?{GuideSelection.TabParameter}={Uri.EscapeDataString(tab.Name)}";
        return subTab is null ? href : $"{href}&{GuideSelection.SubTabParameter}={Uri.EscapeDataString(subTab.Name)}";
    }
}
