using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>
/// What the InfoPage editor's Placement section needs from the Event's tree (P5-24, #208): where this InfoPage
/// is placed, the Tab/Sub Tab choices, what already fills each screen, and what removing the Placement empties.
/// </summary>
/// <param name="tree">The Event's persisted Tiers and Placements.</param>
/// <param name="infoPageId">The InfoPage being edited.</param>
/// <param name="infoPageTitles">Titles of the Event's InfoPages by id, to name what fills a taken screen.</param>
/// <remarks>
/// Unlike <see cref="PlacementTreeModel"/>, choices come from every Tier, not only those with an Activity under
/// them - an InfoPage-only Tab is still a choice.
/// </remarks>
public sealed class InfoPagePlacementPicker(
    PlacementTreeDto tree, Guid infoPageId, IReadOnlyDictionary<Guid, string> infoPageTitles)
{
    private InfoPagePlacementDto? Own => tree.InfoPagePlacements.FirstOrDefault(p => p.InfoPageId == infoPageId);

    /// <summary>This InfoPage's saved Placement id, if any.</summary>
    public Guid? PlacementId => Own?.Id;

    /// <summary>Where this InfoPage is placed, or <see langword="null"/> if it isn't.</summary>
    public PlacementPath? Current =>
        Own is { } own && tree.Tabs.FirstOrDefault(t => t.Id == own.TabId) is { } tab
            ? new PlacementPath(tab.Name, tree.SubTabs.FirstOrDefault(s => s.Id == own.SubTabId)?.Name, null, null)
            : null;

    /// <summary>Every Tab on the Event as a choice.</summary>
    /// <returns>The choices in display order.</returns>
    public IReadOnlyList<TierOption> TabOptions() =>
        [.. tree.Tabs.OrderBy(t => t.SortOrder).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => new TierOption(t.Name, 0))];

    /// <summary>The Sub Tabs under the named Tab as choices - none if the name is new.</summary>
    /// <param name="tabName">The chosen Tab's name.</param>
    /// <returns>The choices in display order.</returns>
    public IReadOnlyList<TierOption> SubTabOptions(string tabName) =>
        FindTab(tabName) is not { } tab
            ? []
            : [.. tree.SubTabs.Where(s => s.TabId == tab.Id)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => new TierOption(s.Name, 0))];

    /// <summary>What already fills the exact (Tab, Sub Tab) screen - blank if it is free or doesn't exist yet.</summary>
    /// <param name="tabName">The Tab's name.</param>
    /// <param name="subTabName">The Sub Tab's name, or <see langword="null"/> for the bare Tab's screen.</param>
    /// <returns>The note to show beside the choice, or an empty string.</returns>
    public string Annotate(string tabName, string? subTabName)
    {
        if (FindTab(tabName) is not { } tab)
        {
            return "";
        }

        SubTabDto? subTab = subTabName is null
            ? null
            : tree.SubTabs.FirstOrDefault(s => s.TabId == tab.Id && PlacementPath.SameName(s.Name, subTabName.Trim()));
        if (subTabName is not null && subTab is null)
        {
            return "";
        }

        if (tree.Placements.Any(p => p.TabId == tab.Id && p.SubTabId == subTab?.Id))
        {
            return "shows Activities";
        }

        InfoPagePlacementDto? other = tree.InfoPagePlacements.FirstOrDefault(
            p => p.InfoPageId != infoPageId && p.TabId == tab.Id && p.SubTabId == subTab?.Id);
        return other is null ? "" : $"shows {infoPageTitles.GetValueOrDefault(other.InfoPageId, "another Info page")}";
    }

    /// <summary>The Tiers removing this InfoPage's Placement would empty, deepest first.</summary>
    /// <returns>The reaped Tiers; empty if none, or if it isn't placed.</returns>
    public IReadOnlyList<TierRef> DescribeRemoval() =>
        Own is { } own ? PlacementTreeModel.DescribeInfoPagePlacementRemoval(tree, own.Id) : [];

    private TabDto? FindTab(string name) => tree.Tabs.FirstOrDefault(t => PlacementPath.SameName(t.Name, name.Trim()));
}
