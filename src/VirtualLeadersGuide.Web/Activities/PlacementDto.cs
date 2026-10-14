namespace VirtualLeadersGuide.Web.Activities;

/// <summary>The dashboard's view of a <c>/api/tabs</c> row (P5-11, #96).</summary>
public sealed class TabDto
{
    /// <summary>This Tab's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>This Tab's display name.</summary>
    public required string Name { get; init; }

    /// <summary>This Tab's position among its Event's other Tabs (ADR-0049).</summary>
    public required int SortOrder { get; init; }
}

/// <summary>The dashboard's view of a <c>/api/subTabs</c> row (P5-11, #96).</summary>
public sealed class SubTabDto
{
    /// <summary>This Sub Tab's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The <see cref="TabDto.Id"/> this Sub Tab is scoped to.</summary>
    public required Guid TabId { get; init; }

    /// <summary>This Sub Tab's display name.</summary>
    public required string Name { get; init; }

    /// <summary>This Sub Tab's position among its Tab's other Sub Tabs (ADR-0049).</summary>
    public required int SortOrder { get; init; }
}

/// <summary>The dashboard's view of a <c>/api/sections</c> row (P5-11, #96).</summary>
public sealed class SectionDto
{
    /// <summary>This Section's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The bare Tab this Section is scoped to - exactly one of this and <see cref="ParentSubTabId"/> is set (ADR-0046's P5-11 amendment).</summary>
    public Guid? ParentTabId { get; init; }

    /// <summary>The Sub Tab this Section is scoped to - exactly one of this and <see cref="ParentTabId"/> is set.</summary>
    public Guid? ParentSubTabId { get; init; }

    /// <summary>This Section's display name.</summary>
    public required string Name { get; init; }

    /// <summary>This Section's position among its parent's other Sections (ADR-0049).</summary>
    public required int SortOrder { get; init; }
}

/// <summary>The dashboard's view of a <c>/api/subSections</c> row (P5-11, #96).</summary>
public sealed class SubSectionDto
{
    /// <summary>This Sub Section's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The <see cref="SectionDto.Id"/> this Sub Section is scoped to.</summary>
    public required Guid SectionId { get; init; }

    /// <summary>This Sub Section's display name.</summary>
    public required string Name { get; init; }

    /// <summary>This Sub Section's position among its Section's other Sub Sections (ADR-0049).</summary>
    public required int SortOrder { get; init; }
}

/// <summary>The dashboard's view of a <c>/api/placements</c> row (P5-11, #96) - the resolved Tier path as ids.</summary>
public sealed class PlacementDto
{
    /// <summary>This Placement's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The Activity this Placement is for.</summary>
    public required Guid ActivityId { get; init; }

    /// <summary>The resolved Tab.</summary>
    public required Guid TabId { get; init; }

    /// <summary>The resolved Sub Tab, if this Placement sets one.</summary>
    public Guid? SubTabId { get; init; }

    /// <summary>The resolved Section, if this Placement sets one.</summary>
    public Guid? SectionId { get; init; }

    /// <summary>The resolved Sub Section, if this Placement sets one.</summary>
    public Guid? SubSectionId { get; init; }

    /// <summary>This Placement's position among others sharing its exact Tier path (ADR-0050).</summary>
    public required int SortOrder { get; init; }
}

/// <summary>One Event's whole Tier structure plus every Placement on it - what <see cref="ApiPlacementClient.GetTreeForEventAsync"/> returns.</summary>
public sealed class PlacementTreeDto
{
    /// <summary>An empty tree - an Event with no Placements yet.</summary>
    public static PlacementTreeDto Empty { get; } = new();

    /// <summary>Every Tab on the Event.</summary>
    public IReadOnlyList<TabDto> Tabs { get; init; } = [];

    /// <summary>Every Sub Tab on the Event.</summary>
    public IReadOnlyList<SubTabDto> SubTabs { get; init; } = [];

    /// <summary>Every Section on the Event.</summary>
    public IReadOnlyList<SectionDto> Sections { get; init; } = [];

    /// <summary>Every Sub Section on the Event.</summary>
    public IReadOnlyList<SubSectionDto> SubSections { get; init; } = [];

    /// <summary>Every Placement on the Event, across all of its Activities.</summary>
    public IReadOnlyList<PlacementDto> Placements { get; init; } = [];

    /// <summary>Each Activity's Placements as name paths, in display order - the Activities list's "Placed under" chips (wireframe turn 1a).</summary>
    /// <returns>A lookup by Activity id; an Activity with no Placement has no entry.</returns>
    public IReadOnlyDictionary<Guid, IReadOnlyList<PlacementPath>> PathsByActivity()
    {
        var tabs = Tabs.ToDictionary(t => t.Id);
        var subTabs = SubTabs.ToDictionary(t => t.Id);
        var sections = Sections.ToDictionary(t => t.Id);
        var subSections = SubSections.ToDictionary(t => t.Id);

        return Placements
            .Where(p => tabs.ContainsKey(p.TabId))
            .GroupBy(p => p.ActivityId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PlacementPath>)[.. group
                    .Select(p => new PlacementPath(
                        tabs[p.TabId].Name,
                        p.SubTabId is { } subTabId ? subTabs.GetValueOrDefault(subTabId)?.Name : null,
                        p.SectionId is { } sectionId ? sections.GetValueOrDefault(sectionId)?.Name : null,
                        p.SubSectionId is { } subSectionId ? subSections.GetValueOrDefault(subSectionId)?.Name : null))
                    .OrderBy(path => path.Display, StringComparer.OrdinalIgnoreCase)]);
    }
}
