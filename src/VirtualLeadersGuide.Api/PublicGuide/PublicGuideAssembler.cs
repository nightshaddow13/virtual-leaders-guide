using JsonApiDotNetCore.Resources;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.PublicGuide;

/// <summary>Nests and orders one Event's Tiers and Placements into the public guide (P5-14, #88).</summary>
/// <remarks>
/// Display order - Tiers by <c>SortOrder</c> then name, Activities at one path by Placement <c>SortOrder</c>
/// then name - and the merging of same-named sibling Tiers (ADR-0080) match the dashboard's tree (Web's
/// <c>PlacementTreeModel</c>). The two can't share code across the project boundary: change one, change the
/// other, and keep their paired test cases (<c>PublicGuideEndpointsShould</c>'s <c>*_ForGetGuide</c> ordering
/// cases and <c>PlacementTreeModelShould</c>'s) in step. A Section or Sub Section with no Activity under it is
/// left out, so a screen never shows a bare heading.
/// </remarks>
/// <param name="tabs">The Event's Tabs.</param>
/// <param name="subTabs">The Event's Sub Tabs.</param>
/// <param name="sections">The Event's Sections.</param>
/// <param name="subSections">The Event's Sub Sections.</param>
/// <param name="placements">The Event's Activity Placements, each with its <see cref="ActivityPlacement.Activity"/> loaded.</param>
/// <param name="infoPagePlacements">The Event's InfoPage Placements, each with its <see cref="InfoPagePlacement.InfoPage"/> loaded.</param>
internal sealed class PublicGuideAssembler(
    IReadOnlyList<Tab> tabs,
    IReadOnlyList<SubTab> subTabs,
    IReadOnlyList<Section> sections,
    IReadOnlyList<SubSection> subSections,
    IReadOnlyList<ActivityPlacement> placements,
    IReadOnlyList<InfoPagePlacement> infoPagePlacements)
{
    /// <summary>Builds the guide.</summary>
    /// <returns>Every Tab with its screens, in display order.</returns>
    public PublicGuideDto Build() => new()
    {
        Tabs = [.. Merged(tabs, t => t.SortOrder, t => t.Name).Select(ToTab)]
    };

    private PublicGuideTabDto ToTab(MergedTier tab) => new()
    {
        Name = tab.Name,
        Screen = ToScreen(new GuidePosition(tab.Ids)),
        SubTabs =
        [
            .. Merged(subTabs.Where(s => tab.Ids.Contains(s.TabId)), s => s.SortOrder, s => s.Name)
                .Select(subTab => new PublicGuideSubTabDto
                {
                    Name = subTab.Name,
                    Screen = ToScreen(new GuidePosition(tab.Ids, subTab.Ids))
                })
        ]
    };

    private PublicGuideScreenDto ToScreen(GuidePosition screen)
    {
        InfoPage? infoPage = infoPagePlacements
            .Where(p => screen.TabIds.Contains(p.TabId!.Value) && GuidePosition.Matches(screen.SubTabIds, p.SubTabId))
            .Select(p => p.InfoPage!)
            .OrderBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (infoPage is not null)
        {
            return new PublicGuideScreenDto
            {
                InfoPage = new PublicInfoPageDto
                {
                    Id = infoPage.Id, Title = infoPage.Title, MarkdownContent = infoPage.MarkdownContent
                },
                Activities = [],
                Sections = []
            };
        }

        IEnumerable<Section> onScreen = sections.Where(s => screen.SubTabIds is null
            ? s.ParentTabId is { } tabId && screen.TabIds.Contains(tabId)
            : s.ParentSubTabId is { } subTabId && screen.SubTabIds.Contains(subTabId));

        return new PublicGuideScreenDto
        {
            Activities = ActivitiesAt(screen),
            Sections =
            [
                .. Merged(onScreen, s => s.SortOrder, s => s.Name)
                    .Select(section => ToSection(section, screen with { SectionIds = section.Ids }))
                    .Where(section => section.Activities.Count > 0 || section.SubSections.Count > 0)
            ]
        };
    }

    private PublicGuideSectionDto ToSection(MergedTier section, GuidePosition position) => new()
    {
        Name = section.Name,
        Activities = ActivitiesAt(position),
        SubSections =
        [
            .. Merged(subSections.Where(s => section.Ids.Contains(s.SectionId)), s => s.SortOrder, s => s.Name)
                .Select(subSection => new PublicGuideSubSectionDto
                {
                    Name = subSection.Name,
                    Activities = ActivitiesAt(position with { SubSectionIds = subSection.Ids })
                })
                .Where(subSection => subSection.Activities.Count > 0)
        ]
    };

    private IReadOnlyList<PublicGuideActivityDto> ActivitiesAt(GuidePosition position) =>
    [
        .. placements
            .Where(position.Holds)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Activity!.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PublicGuideActivityDto { Name = p.Activity!.Name, Description = p.Activity.Description })
    ];

    /// <summary>Sibling Tiers in display order, same-named ones folded into the first of them.</summary>
    private static IEnumerable<MergedTier> Merged<T>(
        IEnumerable<T> siblings, Func<T, int> sortOrder, Func<T, string> name)
        where T : Identifiable<Guid> =>
        siblings
            .OrderBy(sortOrder)
            .ThenBy(name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MergedTier(name(group.First()), [.. group.Select(tier => tier.Id)]));

    /// <summary>One Tier as a visitor sees it, standing for every row sharing its name under one parent.</summary>
    private sealed record MergedTier(string Name, HashSet<Guid> Ids);

    /// <summary>One position in the guide: a screen, or a Section or Sub Section on it. An unset level is null.</summary>
    private sealed record GuidePosition(
        HashSet<Guid> TabIds, HashSet<Guid>? SubTabIds = null,
        HashSet<Guid>? SectionIds = null, HashSet<Guid>? SubSectionIds = null)
    {
        public bool Holds(ActivityPlacement placement) =>
            Matches(TabIds, placement.TabId) && Matches(SubTabIds, placement.SubTabId)
            && Matches(SectionIds, placement.SectionId) && Matches(SubSectionIds, placement.SubSectionId);

        public static bool Matches(HashSet<Guid>? level, Guid? id) =>
            level is null ? id is null : id is { } value && level.Contains(value);
    }
}
