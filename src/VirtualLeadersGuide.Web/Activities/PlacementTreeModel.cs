namespace VirtualLeadersGuide.Web.Activities;

/// <summary>Which Tier a <see cref="TierNode"/> is.</summary>
public enum TierKind
{
    Tab,
    SubTab,
    Section,
    SubSection
}

/// <summary>One Activity sitting under a Tier in the live tree (wireframe turn 2a).</summary>
/// <param name="ActivityId">The placed Activity.</param>
/// <param name="ActivityName">Its display name.</param>
/// <param name="IsThisActivity">Whether this is the Activity whose page is open - drawn lit.</param>
/// <param name="IsPending">Whether this is an unsaved ghost row - drawn dashed.</param>
public sealed record PlacementLeaf(Guid ActivityId, string ActivityName, bool IsThisActivity, bool IsPending)
{
    /// <summary>Always empty - a placed Activity is a leaf. Exists so <c>RadzenTree</c>'s single level can read the same <c>Items</c> property off every row, tier or leaf.</summary>
    public IEnumerable<object> Items => [];
}

/// <summary>One Tier row in the live tree, with whatever hangs beneath it.</summary>
public sealed class TierNode
{
    internal TierNode(TierKind kind, string name, int sortOrder, bool isPending)
    {
        Kind = kind;
        Name = name;
        SortOrder = sortOrder;
        IsPending = isPending;
    }

    /// <summary>Which Tier this is.</summary>
    public TierKind Kind { get; }

    /// <summary>This Tier's display name.</summary>
    public string Name { get; }

    /// <summary>Its position among siblings - server Tiers keep theirs, Tiers only a ghost would create sort last.</summary>
    public int SortOrder { get; }

    /// <summary>Whether this Tier exists only because of an unsaved ghost row - drawn with a NEW badge (wireframe 2b).</summary>
    public bool IsPending { get; }

    /// <summary>Child Tiers, ordered Sub Tabs, Sections, Sub Sections, then by <see cref="SortOrder"/> and name.</summary>
    public IReadOnlyList<TierNode> Children => ChildList;

    /// <summary>Activities placed directly at this Tier.</summary>
    public IReadOnlyList<PlacementLeaf> Leaves => LeafList;

    /// <summary>Everything that hangs beneath this Tier - its placed Activities, then its child Tiers - as one list, the shape <c>RadzenTree</c>'s level template walks.</summary>
    public IEnumerable<object> Items => LeafList.Cast<object>().Concat(ChildList);

    internal List<TierNode> ChildList { get; } = [];

    internal List<PlacementLeaf> LeafList { get; } = [];
}

/// <summary>One autofill choice at a level, with how many Activities are placed at or under it ("6 activities", wireframe 1e-2).</summary>
/// <param name="Name">The Tier's name.</param>
/// <param name="ActivityCount">Distinct Activities placed at or under it, ghosts included.</param>
public sealed record TierOption(string Name, int ActivityCount);

/// <summary>One level of a resolved path, for the NEW badges (wireframe 1e-7).</summary>
/// <param name="Kind">Which Tier this level is.</param>
/// <param name="Name">The name as it will be sent (canonicalized to an existing Tier's casing when one matches).</param>
/// <param name="IsNew">Whether saving would create this Tier - false when it exists on the server or an earlier ghost row already introduces it.</param>
public sealed record PathLevel(TierKind Kind, string Name, bool IsNew);

/// <summary>
/// The Activity page's working model of an Event's Tier tree: what the server holds, merged with this
/// session's unsaved ghost rows (P5-11, #96; plan decision 4). Drives the live tree, every autofill level's
/// choices, canonical casing, the "did you mean" nudge, and duplicate detection - all client-side, so a
/// second "Place it" can reuse a Tier an earlier ghost introduced, and an exact repeat is caught before
/// Save rather than as a 409 from it.
/// </summary>
/// <remarks>
/// Built purely from Placement paths: a Tier only exists while something is placed under it (ADR-0046), so
/// the tree is the union of every Placement's path. Matching is by name, case-insensitively, scoped by the
/// parent chain - the same rules <c>ActivityPlacementResourceDefinition</c> applies server-side, so the two
/// can't disagree about whether a typed name is an existing Tier.
/// </remarks>
public sealed class PlacementTreeModel
{
    private const int PendingSortOrder = int.MaxValue;

    private readonly List<TierNode> _roots = [];
    private readonly HashSet<PlacementPath> _thisActivitysPaths = [];

    /// <summary>Builds the merged model.</summary>
    /// <param name="server">The Event's persisted Tiers and Placements.</param>
    /// <param name="activityId">The Activity whose page is open.</param>
    /// <param name="activityName">That Activity's name, shown on its ghost rows.</param>
    /// <param name="activityNames">Every Activity on the Event by id, for the other rows in the tree.</param>
    /// <param name="ghosts">This session's not-yet-saved Placements for <paramref name="activityId"/>, oldest first.</param>
    public PlacementTreeModel(
        PlacementTreeDto server, Guid activityId, string activityName,
        IReadOnlyDictionary<Guid, string> activityNames, IReadOnlyList<PlacementPath> ghosts)
    {
        var tabs = server.Tabs.ToDictionary(t => t.Id);
        var subTabs = server.SubTabs.ToDictionary(t => t.Id);
        var sections = server.Sections.ToDictionary(t => t.Id);
        var subSections = server.SubSections.ToDictionary(t => t.Id);

        foreach (PlacementDto placement in server.Placements)
        {
            if (!tabs.TryGetValue(placement.TabId, out TabDto? tab))
            {
                continue;
            }

            SubTabDto? subTab = placement.SubTabId is { } subTabId ? subTabs.GetValueOrDefault(subTabId) : null;
            SectionDto? section = placement.SectionId is { } sectionId ? sections.GetValueOrDefault(sectionId) : null;
            SubSectionDto? subSection = placement.SubSectionId is { } subSectionId ? subSections.GetValueOrDefault(subSectionId) : null;

            var path = new PlacementPath(tab.Name, subTab?.Name, section?.Name, subSection?.Name);
            TierNode deepest = Insert(path, pending: false,
                tab.SortOrder, subTab?.SortOrder ?? 0, section?.SortOrder ?? 0, subSection?.SortOrder ?? 0);

            bool isThis = placement.ActivityId == activityId;
            deepest.LeafList.Add(new PlacementLeaf(
                placement.ActivityId, isThis ? activityName : activityNames.GetValueOrDefault(placement.ActivityId, "(unknown activity)"),
                isThis, IsPending: false));

            if (isThis)
            {
                _thisActivitysPaths.Add(path);
            }
        }

        foreach (PlacementPath ghost in ghosts)
        {
            PlacementPath path = Canonicalize(ghost);
            TierNode deepest = Insert(path, pending: true, PendingSortOrder, PendingSortOrder, PendingSortOrder, PendingSortOrder);
            deepest.LeafList.Add(new PlacementLeaf(activityId, activityName, IsThisActivity: true, IsPending: true));
            _thisActivitysPaths.Add(path);
        }

        SortRecursively(_roots);
    }

    /// <summary>The tree's top level - every Tab - ordered by <see cref="TierNode.SortOrder"/> then name.</summary>
    public IReadOnlyList<TierNode> Roots => _roots;

    /// <summary>Every Tab as an autofill choice.</summary>
    public IReadOnlyList<TierOption> TabOptions() => ToOptions(_roots);

    /// <summary>The Sub Tabs under <paramref name="tab"/>, or none if that Tab doesn't exist yet.</summary>
    public IReadOnlyList<TierOption> SubTabOptions(string tab) =>
        ToOptions(FindTab(tab)?.ChildList.Where(c => c.Kind == TierKind.SubTab));

    /// <summary>
    /// The Sections under the chosen parent: <paramref name="subTab"/> when one is set, otherwise the bare
    /// <paramref name="tab"/> - each level only ever lists children of the level above (wireframe 2b).
    /// </summary>
    public IReadOnlyList<TierOption> SectionOptions(string tab, string? subTab) =>
        ToOptions(FindSectionParent(tab, subTab)?.ChildList.Where(c => c.Kind == TierKind.Section));

    /// <summary>The Sub Sections under the Section at the given path.</summary>
    public IReadOnlyList<TierOption> SubSectionOptions(string tab, string? subTab, string section) =>
        ToOptions(FindSectionParent(tab, subTab)?.ChildList
            .FirstOrDefault(c => c.Kind == TierKind.Section && PlacementPath.SameName(c.Name, section))
            ?.ChildList.Where(c => c.Kind == TierKind.SubSection));

    /// <summary>
    /// Rewrites each segment of <paramref name="path"/> to an existing Tier's casing where one matches at
    /// that point in the chain - what Api would resolve it to anyway. Segments past the first that doesn't
    /// match keep their typed casing.
    /// </summary>
    public PlacementPath Canonicalize(PlacementPath path)
    {
        TierNode? tab = FindTab(path.Tab);
        TierNode? subTab = path.SubTab is null ? null : Find(tab, TierKind.SubTab, path.SubTab);
        TierNode? sectionParent = path.SubTab is null ? tab : subTab;
        TierNode? section = path.Section is null ? null : Find(sectionParent, TierKind.Section, path.Section);
        TierNode? subSection = path.SubSection is null ? null : Find(section, TierKind.SubSection, path.SubSection);

        return new PlacementPath(
            tab?.Name ?? path.Tab, subTab?.Name ?? path.SubTab, section?.Name ?? path.Section, subSection?.Name ?? path.SubSection);
    }

    /// <summary>Whether this Activity is already placed at exactly <paramref name="path"/> - on the server or as a ghost (ADR-0046's rule, applied live; wireframe 1e-8).</summary>
    public bool IsAlreadyPlaced(PlacementPath path) => _thisActivitysPaths.Contains(path);

    /// <summary>Each level of <paramref name="path"/> with whether saving would create it - for the NEW badges.</summary>
    public IReadOnlyList<PathLevel> Describe(PlacementPath path)
    {
        var levels = new List<PathLevel>();
        TierNode? tab = FindTab(path.Tab);
        levels.Add(new PathLevel(TierKind.Tab, tab?.Name ?? path.Tab, tab is null));

        TierNode? parent = tab;
        TierNode? sectionParent = tab;
        if (path.SubTab is not null)
        {
            TierNode? subTab = Find(parent, TierKind.SubTab, path.SubTab);
            levels.Add(new PathLevel(TierKind.SubTab, subTab?.Name ?? path.SubTab, subTab is null));
            sectionParent = subTab;
        }

        if (path.Section is not null)
        {
            TierNode? section = Find(sectionParent, TierKind.Section, path.Section);
            levels.Add(new PathLevel(TierKind.Section, section?.Name ?? path.Section, section is null));

            if (path.SubSection is not null)
            {
                TierNode? subSection = Find(section, TierKind.SubSection, path.SubSection);
                levels.Add(new PathLevel(TierKind.SubSection, subSection?.Name ?? path.SubSection, subSection is null));
            }
        }

        return levels;
    }

    /// <summary>
    /// The existing option <paramref name="typed"/> matches ignoring case but not exactly - the "did you
    /// mean Waterfront?" nudge (wireframe 1e-8) - or <see langword="null"/> when there's no near-miss.
    /// </summary>
    /// <param name="options">The level's current choices.</param>
    /// <param name="typed">What the user typed.</param>
    public static string? NearDuplicateOf(IReadOnlyList<TierOption> options, string? typed)
    {
        string? trimmed = typed?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        TierOption? match = options.FirstOrDefault(o => PlacementPath.SameName(o.Name, trimmed));
        return match is not null && !string.Equals(match.Name, trimmed, StringComparison.Ordinal) ? match.Name : null;
    }

    private TierNode? FindTab(string name) => Find(null, TierKind.Tab, name);

    private TierNode? FindSectionParent(string tab, string? subTab)
    {
        TierNode? tabNode = FindTab(tab);
        return subTab is null ? tabNode : Find(tabNode, TierKind.SubTab, subTab);
    }

    private TierNode? Find(TierNode? parent, TierKind kind, string name)
    {
        IEnumerable<TierNode> candidates = parent is null ? (kind == TierKind.Tab ? _roots : []) : parent.ChildList;
        return candidates.FirstOrDefault(c => c.Kind == kind && PlacementPath.SameName(c.Name, name));
    }

    private static List<TierOption> ToOptions(IEnumerable<TierNode>? nodes) =>
        nodes is null ? [] : [.. nodes.Select(n => new TierOption(n.Name, CountActivities(n)))];

    private static int CountActivities(TierNode node)
    {
        var ids = new HashSet<Guid>();
        Collect(node, ids);
        return ids.Count;

        static void Collect(TierNode n, HashSet<Guid> into)
        {
            foreach (PlacementLeaf leaf in n.LeafList)
            {
                into.Add(leaf.ActivityId);
            }

            foreach (TierNode child in n.ChildList)
            {
                Collect(child, into);
            }
        }
    }

    private TierNode Insert(PlacementPath path, bool pending, int tabOrder, int subTabOrder, int sectionOrder, int subSectionOrder)
    {
        TierNode tab = GetOrAdd(_roots, TierKind.Tab, path.Tab, tabOrder, pending);
        TierNode deepest = tab;
        TierNode sectionParent = tab;

        if (path.SubTab is not null)
        {
            deepest = sectionParent = GetOrAdd(tab.ChildList, TierKind.SubTab, path.SubTab, subTabOrder, pending);
        }

        if (path.Section is not null)
        {
            deepest = GetOrAdd(sectionParent.ChildList, TierKind.Section, path.Section, sectionOrder, pending);

            if (path.SubSection is not null)
            {
                deepest = GetOrAdd(deepest.ChildList, TierKind.SubSection, path.SubSection, subSectionOrder, pending);
            }
        }

        return deepest;
    }

    private static TierNode GetOrAdd(List<TierNode> siblings, TierKind kind, string name, int sortOrder, bool pending)
    {
        TierNode? existing = siblings.FirstOrDefault(s => s.Kind == kind && PlacementPath.SameName(s.Name, name));
        if (existing is not null)
        {
            return existing;
        }

        var created = new TierNode(kind, name, sortOrder, pending);
        siblings.Add(created);
        return created;
    }

    private static void SortRecursively(List<TierNode> nodes)
    {
        nodes.Sort((a, b) =>
        {
            int byKind = a.Kind.CompareTo(b.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            int byOrder = a.SortOrder.CompareTo(b.SortOrder);
            return byOrder != 0 ? byOrder : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (TierNode node in nodes)
        {
            SortRecursively(node.ChildList);
            node.LeafList.Sort((a, b) => string.Compare(a.ActivityName, b.ActivityName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
