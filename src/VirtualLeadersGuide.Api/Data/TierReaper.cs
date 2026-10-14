using Microsoft.EntityFrameworkCore;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Marks for deletion every <see cref="Tab"/>/<see cref="SubTab"/>/<see cref="Section"/>/<see cref="SubSection"/>
/// that nothing references once a set of <see cref="ActivityPlacement"/> rows is gone - ADR-0046's
/// lazy-create, auto-delete lifecycle, cascading up through all four levels (P5-12, #97).
/// </summary>
/// <remarks>
/// <para>
/// A Tier is referenced by any surviving Placement that names it, or by any surviving child Tier: a Section
/// survives through a Sub Section that still has a Placement, a Sub Tab through a Sub Tab-parented Section,
/// and a Tab through a Sub Tab or a Tab-parented Section. Levels are decided bottom-up, so a Tier whose only
/// remaining children are themselves being reaped is reaped too.
/// </para>
/// <para>
/// Only ever adds removals to the <see cref="VirtualLeadersGuideDbContext"/> change tracker and never calls
/// <c>SaveChangesAsync</c> - the caller's own save deletes the Placements and the emptied Tiers together, in
/// one transaction (ADR-0072's single write path). Takes a collection so deleting a whole Activity (P5-9,
/// #95) can pass every one of its Placements through the same logic.
/// </para>
/// </remarks>
public sealed class TierReaper(VirtualLeadersGuideDbContext dbContext)
{
    /// <summary>
    /// Marks every Tier left unreferenced by removing <paramref name="removing"/> for deletion.
    /// </summary>
    /// <param name="removing">
    /// The Placements about to be deleted. Must be the stored rows (real Tier foreign keys), not
    /// JsonApiDotNetCore's id-only placeholder.
    /// </param>
    /// <param name="cancellationToken">Propagated to the underlying queries.</param>
    public async Task ReapEmptiedTiersAsync(IReadOnlyCollection<ActivityPlacement> removing, CancellationToken cancellationToken)
    {
        if (removing.Count == 0)
        {
            return;
        }

        Guid[] removingIds = [.. removing.Select(placement => placement.Id)];
        IQueryable<ActivityPlacement> surviving = dbContext.ActivityPlacements.AsNoTracking()
            .Where(placement => !removingIds.Contains(placement.Id));

        List<Guid> subSectionIds = await ReapSubSectionsAsync(removing, surviving, cancellationToken);
        List<Guid> sectionIds = await ReapSectionsAsync(removing, surviving, subSectionIds, cancellationToken);
        List<Guid> subTabIds = await ReapSubTabsAsync(removing, surviving, sectionIds, cancellationToken);
        await ReapTabsAsync(removing, surviving, subTabIds, sectionIds, cancellationToken);
    }

    private async Task<List<Guid>> ReapSubSectionsAsync(
        IReadOnlyCollection<ActivityPlacement> removing, IQueryable<ActivityPlacement> surviving, CancellationToken cancellationToken)
    {
        Guid[] candidates = [.. removing.Where(p => p.SubSectionId is not null).Select(p => p.SubSectionId!.Value).Distinct()];
        List<Guid> stillUsed = await surviving.Where(p => p.SubSectionId != null && candidates.Contains(p.SubSectionId.Value))
            .Select(p => p.SubSectionId!.Value).Distinct().ToListAsync(cancellationToken);

        Guid[] reaped = [.. candidates.Except(stillUsed)];
        dbContext.SubSections.RemoveRange(await dbContext.SubSections.Where(s => reaped.Contains(s.Id)).ToListAsync(cancellationToken));
        return [.. reaped];
    }

    private async Task<List<Guid>> ReapSectionsAsync(
        IReadOnlyCollection<ActivityPlacement> removing, IQueryable<ActivityPlacement> surviving,
        List<Guid> reapedSubSectionIds, CancellationToken cancellationToken)
    {
        Guid[] candidates = [.. removing.Where(p => p.SectionId is not null).Select(p => p.SectionId!.Value).Distinct()];
        List<Guid> usedByPlacement = await surviving.Where(p => p.SectionId != null && candidates.Contains(p.SectionId.Value))
            .Select(p => p.SectionId!.Value).Distinct().ToListAsync(cancellationToken);
        List<Guid> usedByChild = await dbContext.SubSections.AsNoTracking()
            .Where(s => candidates.Contains(s.SectionId) && !reapedSubSectionIds.Contains(s.Id))
            .Select(s => s.SectionId).Distinct().ToListAsync(cancellationToken);

        Guid[] reaped = [.. candidates.Except(usedByPlacement).Except(usedByChild)];
        dbContext.Sections.RemoveRange(await dbContext.Sections.Where(s => reaped.Contains(s.Id)).ToListAsync(cancellationToken));
        return [.. reaped];
    }

    private async Task<List<Guid>> ReapSubTabsAsync(
        IReadOnlyCollection<ActivityPlacement> removing, IQueryable<ActivityPlacement> surviving,
        List<Guid> reapedSectionIds, CancellationToken cancellationToken)
    {
        Guid[] candidates = [.. removing.Where(p => p.SubTabId is not null).Select(p => p.SubTabId!.Value).Distinct()];
        List<Guid> usedByPlacement = await surviving.Where(p => p.SubTabId != null && candidates.Contains(p.SubTabId.Value))
            .Select(p => p.SubTabId!.Value).Distinct().ToListAsync(cancellationToken);
        List<Guid> usedByChild = await dbContext.Sections.AsNoTracking()
            .Where(s => s.ParentSubTabId != null && candidates.Contains(s.ParentSubTabId.Value) && !reapedSectionIds.Contains(s.Id))
            .Select(s => s.ParentSubTabId!.Value).Distinct().ToListAsync(cancellationToken);

        Guid[] reaped = [.. candidates.Except(usedByPlacement).Except(usedByChild)];
        dbContext.SubTabs.RemoveRange(await dbContext.SubTabs.Where(s => reaped.Contains(s.Id)).ToListAsync(cancellationToken));
        return [.. reaped];
    }

    private async Task ReapTabsAsync(
        IReadOnlyCollection<ActivityPlacement> removing, IQueryable<ActivityPlacement> surviving,
        List<Guid> reapedSubTabIds, List<Guid> reapedSectionIds, CancellationToken cancellationToken)
    {
        Guid[] candidates = [.. removing.Where(p => p.TabId is not null).Select(p => p.TabId!.Value).Distinct()];
        List<Guid> usedByPlacement = await surviving.Where(p => p.TabId != null && candidates.Contains(p.TabId.Value))
            .Select(p => p.TabId!.Value).Distinct().ToListAsync(cancellationToken);
        List<Guid> usedBySubTab = await dbContext.SubTabs.AsNoTracking()
            .Where(s => candidates.Contains(s.TabId) && !reapedSubTabIds.Contains(s.Id))
            .Select(s => s.TabId).Distinct().ToListAsync(cancellationToken);
        List<Guid> usedBySection = await dbContext.Sections.AsNoTracking()
            .Where(s => s.ParentTabId != null && candidates.Contains(s.ParentTabId.Value) && !reapedSectionIds.Contains(s.Id))
            .Select(s => s.ParentTabId!.Value).Distinct().ToListAsync(cancellationToken);

        Guid[] reaped = [.. candidates.Except(usedByPlacement).Except(usedBySubTab).Except(usedBySection)];
        dbContext.Tabs.RemoveRange(await dbContext.Tabs.Where(t => reaped.Contains(t.Id)).ToListAsync(cancellationToken));
    }
}
