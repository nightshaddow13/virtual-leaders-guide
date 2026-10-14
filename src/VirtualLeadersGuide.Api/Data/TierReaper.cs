using JsonApiDotNetCore.Resources;
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
/// <para>
/// The Web project predicts this outcome to decide whether a removal needs a confirm dialog
/// (<c>PlacementTreeModel.DescribeRemoval</c>) and cannot share this code across the project boundary. The two
/// must apply the same reference rules: change one, change the other, and keep their paired test cases
/// (<c>PlacementsResourceShould</c> / <c>PlacementTreeModelShould</c>) in step.
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

        Guid[] CandidatesOf(Func<ActivityPlacement, Guid?> pick) => [.. removing.Select(pick).OfType<Guid>().Distinct()];

        Guid[] subSections = await ReapAsync(dbContext.SubSections, CandidatesOf(p => p.SubSectionId), cancellationToken,
            ids => surviving.Where(p => p.SubSectionId != null && ids.Contains(p.SubSectionId.Value)).Select(p => p.SubSectionId!.Value));

        Guid[] sections = await ReapAsync(dbContext.Sections, CandidatesOf(p => p.SectionId), cancellationToken,
            ids => surviving.Where(p => p.SectionId != null && ids.Contains(p.SectionId.Value)).Select(p => p.SectionId!.Value),
            ids => dbContext.SubSections.AsNoTracking()
                .Where(s => ids.Contains(s.SectionId) && !subSections.Contains(s.Id)).Select(s => s.SectionId));

        Guid[] subTabs = await ReapAsync(dbContext.SubTabs, CandidatesOf(p => p.SubTabId), cancellationToken,
            ids => surviving.Where(p => p.SubTabId != null && ids.Contains(p.SubTabId.Value)).Select(p => p.SubTabId!.Value),
            ids => dbContext.Sections.AsNoTracking()
                .Where(s => s.ParentSubTabId != null && ids.Contains(s.ParentSubTabId.Value) && !sections.Contains(s.Id))
                .Select(s => s.ParentSubTabId!.Value));

        await ReapAsync(dbContext.Tabs, CandidatesOf(p => p.TabId), cancellationToken,
            ids => surviving.Where(p => p.TabId != null && ids.Contains(p.TabId.Value)).Select(p => p.TabId!.Value),
            ids => dbContext.SubTabs.AsNoTracking()
                .Where(s => ids.Contains(s.TabId) && !subTabs.Contains(s.Id)).Select(s => s.TabId),
            ids => dbContext.Sections.AsNoTracking()
                .Where(s => s.ParentTabId != null && ids.Contains(s.ParentTabId.Value) && !sections.Contains(s.Id))
                .Select(s => s.ParentTabId!.Value));
    }

    /// <summary>
    /// Marks every candidate Tier of one level for deletion unless something still references it.
    /// </summary>
    /// <param name="tiers">The level's set.</param>
    /// <param name="candidates">Ids of this level's Tiers that the removed Placements referenced.</param>
    /// <param name="cancellationToken">Propagated to the underlying queries.</param>
    /// <param name="usedBy">
    /// One query per kind of surviving reference (a Placement naming the Tier, or a child Tier not itself
    /// being reaped), each returning the ids among the candidates it still uses.
    /// </param>
    /// <returns>The ids marked for deletion, so the next level up can discount them as children.</returns>
    private static async Task<Guid[]> ReapAsync<TTier>(
        DbSet<TTier> tiers, Guid[] candidates, CancellationToken cancellationToken, params Func<Guid[], IQueryable<Guid>>[] usedBy)
        where TTier : Identifiable<Guid>
    {
        var stillUsed = new HashSet<Guid>();
        foreach (Func<Guid[], IQueryable<Guid>> query in usedBy)
        {
            stillUsed.UnionWith(await query(candidates).Distinct().ToListAsync(cancellationToken));
        }

        Guid[] reaped = [.. candidates.Where(id => !stillUsed.Contains(id))];
        tiers.RemoveRange(await tiers.Where(tier => reaped.Contains(tier.Id)).ToListAsync(cancellationToken));
        return reaped;
    }
}
