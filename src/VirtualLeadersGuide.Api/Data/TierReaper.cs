using JsonApiDotNetCore.Resources;
using Microsoft.EntityFrameworkCore;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Marks for deletion every <see cref="Tab"/>/<see cref="SubTab"/>/<see cref="Section"/>/<see cref="SubSection"/>
/// that nothing references once a set of Placement rows is gone - ADR-0046's lazy-create, auto-delete
/// lifecycle, cascading up through all four levels (P5-12, #97).
/// </summary>
/// <remarks>
/// <para>
/// A Tier is referenced by any surviving Placement that names it - <see cref="ActivityPlacement"/> or
/// <see cref="InfoPagePlacement"/> (P5-24, #208) - or by any surviving child Tier: a Section survives through a
/// Sub Section that still has a Placement, a Sub Tab through a Sub Tab-parented Section, and a Tab through a Sub
/// Tab or a Tab-parented Section. Levels are decided bottom-up, so a Tier whose only remaining children are
/// themselves being reaped is reaped too.
/// </para>
/// <para>
/// Only ever adds removals to the <see cref="VirtualLeadersGuideDbContext"/> change tracker and never calls
/// <c>SaveChangesAsync</c> - the caller's own save deletes the Placements and the emptied Tiers together, in
/// one transaction (ADR-0072). Takes a collection so deleting a whole Activity
/// (<see cref="ActivityResourceDefinition"/>, P5-9 #95) passes every one of its Placements through the same
/// logic that <see cref="ActivityPlacementResourceDefinition"/> applies to one.
/// </para>
/// <para>
/// The Web project predicts this outcome to decide what a removal's or a deletion's confirm dialog says
/// (<c>PlacementTreeModel.DescribeRemoval</c> / <c>DescribeDeletion</c> / <c>DescribeInfoPagePlacementRemoval</c>)
/// and cannot share this code across the project boundary. The two must apply the same reference rules: change
/// one, change the other, and keep their paired test cases (<c>PlacementsResourceShould</c> /
/// <c>ActivitiesResourceShould</c> / <c>InfoPagePlacementsResourceShould</c> / <c>PlacementTreeModelShould</c>)
/// in step.
/// </para>
/// </remarks>
public sealed class TierReaper(VirtualLeadersGuideDbContext dbContext)
{
    /// <summary>The Tier ids one removed Placement held.</summary>
    private readonly record struct HeldTiers(Guid? TabId, Guid? SubTabId, Guid? SectionId, Guid? SubSectionId);

    /// <summary>
    /// Marks every Tier left unreferenced by removing <paramref name="removing"/> for deletion.
    /// </summary>
    /// <param name="removing">
    /// The Placements about to be deleted. Must be the stored rows (real Tier foreign keys), not
    /// JsonApiDotNetCore's id-only placeholder.
    /// </param>
    /// <param name="cancellationToken">Propagated to the underlying queries.</param>
    public Task ReapEmptiedTiersAsync(IReadOnlyCollection<ActivityPlacement> removing, CancellationToken cancellationToken) =>
        ReapAsync(
            [.. removing.Select(p => new HeldTiers(p.TabId, p.SubTabId, p.SectionId, p.SubSectionId))],
            [.. removing.Select(p => p.Id)],
            [],
            keep: null,
            cancellationToken);

    /// <summary>
    /// Marks every Tier left unreferenced once <paramref name="removing"/> no longer holds its stored path -
    /// because it is being deleted, or moved elsewhere.
    /// </summary>
    /// <param name="removing">The stored rows, with the Tier foreign keys they hold <em>before</em> this write.</param>
    /// <param name="keep">
    /// Tiers a move is about to point at. The moved row is excluded from the surviving references and its new
    /// path isn't saved yet, so without this a move within one Tab would reap that Tab.
    /// </param>
    /// <param name="cancellationToken">Propagated to the underlying queries.</param>
    public Task ReapEmptiedTiersAsync(
        IReadOnlyCollection<InfoPagePlacement> removing, IReadOnlySet<Guid>? keep, CancellationToken cancellationToken) =>
        ReapAsync(
            [.. removing.Select(p => new HeldTiers(p.TabId, p.SubTabId, SectionId: null, SubSectionId: null))],
            [],
            [.. removing.Select(p => p.Id)],
            keep,
            cancellationToken);

    private async Task ReapAsync(
        HeldTiers[] removing, Guid[] removingActivityPlacementIds, Guid[] removingInfoPagePlacementIds,
        IReadOnlySet<Guid>? keep, CancellationToken cancellationToken)
    {
        if (removing.Length == 0)
        {
            return;
        }

        IQueryable<ActivityPlacement> surviving = dbContext.ActivityPlacements.AsNoTracking()
            .Where(placement => !removingActivityPlacementIds.Contains(placement.Id));
        IQueryable<InfoPagePlacement> survivingInfoPages = dbContext.InfoPagePlacements.AsNoTracking()
            .Where(placement => !removingInfoPagePlacementIds.Contains(placement.Id));

        Guid[] CandidatesOf(Func<HeldTiers, Guid?> pick) =>
            [.. removing.Select(pick).OfType<Guid>().Distinct().Where(id => keep?.Contains(id) != true)];

        IQueryable<Guid> PlacementsAtSubSection(Guid[] ids) =>
            surviving.Where(p => p.SubSectionId != null && ids.Contains(p.SubSectionId.Value)).Select(p => p.SubSectionId!.Value);
        IQueryable<Guid> PlacementsAtSection(Guid[] ids) =>
            surviving.Where(p => p.SectionId != null && ids.Contains(p.SectionId.Value)).Select(p => p.SectionId!.Value);
        IQueryable<Guid> PlacementsAtSubTab(Guid[] ids) =>
            surviving.Where(p => p.SubTabId != null && ids.Contains(p.SubTabId.Value)).Select(p => p.SubTabId!.Value);
        IQueryable<Guid> PlacementsAtTab(Guid[] ids) =>
            surviving.Where(p => p.TabId != null && ids.Contains(p.TabId.Value)).Select(p => p.TabId!.Value);
        IQueryable<Guid> InfoPagesAtSubTab(Guid[] ids) =>
            survivingInfoPages.Where(p => p.SubTabId != null && ids.Contains(p.SubTabId.Value)).Select(p => p.SubTabId!.Value);
        IQueryable<Guid> InfoPagesAtTab(Guid[] ids) =>
            survivingInfoPages.Where(p => p.TabId != null && ids.Contains(p.TabId.Value)).Select(p => p.TabId!.Value);

        Guid[] subSections = await ReapLevelAsync(dbContext.SubSections, CandidatesOf(p => p.SubSectionId), cancellationToken,
            PlacementsAtSubSection);

        IQueryable<Guid> SectionsWithSurvivingSubSections(Guid[] ids) => dbContext.SubSections.AsNoTracking()
            .Where(s => ids.Contains(s.SectionId) && !subSections.Contains(s.Id)).Select(s => s.SectionId);
        Guid[] sections = await ReapLevelAsync(dbContext.Sections, CandidatesOf(p => p.SectionId), cancellationToken,
            PlacementsAtSection, SectionsWithSurvivingSubSections);

        IQueryable<Guid> SubTabsWithSurvivingSections(Guid[] ids) => dbContext.Sections.AsNoTracking()
            .Where(s => s.ParentSubTabId != null && ids.Contains(s.ParentSubTabId.Value) && !sections.Contains(s.Id))
            .Select(s => s.ParentSubTabId!.Value);
        Guid[] subTabs = await ReapLevelAsync(dbContext.SubTabs, CandidatesOf(p => p.SubTabId), cancellationToken,
            PlacementsAtSubTab, InfoPagesAtSubTab, SubTabsWithSurvivingSections);

        IQueryable<Guid> TabsWithSurvivingSubTabs(Guid[] ids) => dbContext.SubTabs.AsNoTracking()
            .Where(s => ids.Contains(s.TabId) && !subTabs.Contains(s.Id)).Select(s => s.TabId);
        IQueryable<Guid> TabsWithSurvivingSections(Guid[] ids) => dbContext.Sections.AsNoTracking()
            .Where(s => s.ParentTabId != null && ids.Contains(s.ParentTabId.Value) && !sections.Contains(s.Id))
            .Select(s => s.ParentTabId!.Value);
        await ReapLevelAsync(dbContext.Tabs, CandidatesOf(p => p.TabId), cancellationToken,
            PlacementsAtTab, InfoPagesAtTab, TabsWithSurvivingSubTabs, TabsWithSurvivingSections);
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
    private static async Task<Guid[]> ReapLevelAsync<TTier>(
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
