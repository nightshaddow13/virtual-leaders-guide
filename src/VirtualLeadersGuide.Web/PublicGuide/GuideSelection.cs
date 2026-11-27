using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.PublicGuide;

/// <summary>Which screen of the public guide a <c>tab</c>/<c>sub</c> query string lands on (ADR-0080).</summary>
/// <param name="Tab">The selected Tab.</param>
/// <param name="SubTab">The selected Sub Tab, or <see langword="null"/> on the bare Tab's screen.</param>
/// <param name="WasStale">Whether the query asked for something the guide no longer has, so this is a fallback.</param>
public sealed record GuideSelection(PublicGuideTabDto Tab, PublicGuideSubTabDto? SubTab, bool WasStale)
{
    /// <summary>The query string parameter naming the Tab. Part of every shared link (ADR-0080).</summary>
    public const string TabParameter = "tab";

    /// <summary>The query string parameter naming the Sub Tab. Part of every shared link (ADR-0080).</summary>
    public const string SubTabParameter = "sub";

    /// <summary>The selected screen.</summary>
    public PublicGuideScreenDto Screen => SubTab?.Screen ?? Tab.Screen;

    /// <summary>Resolves a query string against the guide.</summary>
    /// <param name="guide">The Event's guide.</param>
    /// <param name="tab">The requested Tab name, if any.</param>
    /// <param name="sub">The requested Sub Tab name, if any.</param>
    /// <returns>The selection, or <see langword="null"/> when the guide has no Tabs.</returns>
    public static GuideSelection? Resolve(PublicGuideDto guide, string? tab, string? sub)
    {
        if (guide.Tabs.Count == 0)
        {
            return null;
        }

        bool askedForTab = !string.IsNullOrWhiteSpace(tab);
        PublicGuideTabDto? requested = askedForTab ? guide.Tabs.FirstOrDefault(t => SameName(t.Name, tab)) : null;

        if (askedForTab && requested is null)
        {
            PublicGuideTabDto first = guide.Tabs[0];
            return new GuideSelection(first, DefaultSubTab(first), WasStale: true);
        }

        PublicGuideTabDto selected = requested ?? guide.Tabs[0];
        PublicGuideSubTabDto? fallback = DefaultSubTab(selected);

        if (!string.IsNullOrWhiteSpace(sub))
        {
            PublicGuideSubTabDto? subTab = selected.SubTabs.FirstOrDefault(s => SameName(s.Name, sub));
            return new GuideSelection(selected, subTab ?? fallback, WasStale: subTab is null);
        }

        return new GuideSelection(selected, fallback, WasStale: askedForTab && fallback is not null);
    }

    /// <summary>
    /// The Sub Tab a Tab opens on: none (its bare screen) unless that screen is empty and it has Sub Tabs.
    /// </summary>
    /// <param name="tab">The Tab being opened.</param>
    /// <returns>The first Sub Tab, or <see langword="null"/> for the bare screen.</returns>
    public static PublicGuideSubTabDto? DefaultSubTab(PublicGuideTabDto tab) =>
        tab.Screen.IsEmpty && tab.SubTabs.Count > 0 ? tab.SubTabs[0] : null;

    private static bool SameName(string name, string? asked) =>
        string.Equals(name, asked?.Trim(), StringComparison.OrdinalIgnoreCase);
}
