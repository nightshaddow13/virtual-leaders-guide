using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Resolves a Placement write's Tab and Sub Tab - each from a supplied id or name, creating the row when a name
/// matches none. Shared by both Placement kinds (ADR-0072's amendment).
/// </summary>
/// <remarks>
/// Only ever adds to the <see cref="VirtualLeadersGuideDbContext"/> change tracker and never saves: the
/// caller's own save persists a new Tier together with its first Placement, or neither.
/// </remarks>
/// <param name="dbContext">Looks Tiers up and tracks any it creates.</param>
public sealed class TierPathResolver(VirtualLeadersGuideDbContext dbContext)
{
    /// <summary>Resolves the Tab, which every Placement requires.</summary>
    /// <param name="eventId">The Event the Tab belongs to.</param>
    /// <param name="suppliedId">An existing Tab's id, which wins over <paramref name="suppliedName"/>.</param>
    /// <param name="suppliedName">A Tab's name, matched case-insensitively.</param>
    /// <param name="cancellationToken">Propagated to the underlying queries.</param>
    /// <returns>The existing or newly tracked Tab's id.</returns>
    public async Task<Guid> ResolveTabAsync(Guid eventId, Guid? suppliedId, string? suppliedName, CancellationToken cancellationToken)
    {
        if (suppliedId is { } tabId)
        {
            bool belongsToEvent = await dbContext.Tabs.AsNoTracking()
                .AnyAsync(tab => tab.Id == tabId && tab.EventId == eventId, cancellationToken);

            if (!belongsToEvent)
            {
                throw PlacementErrors.Unprocessable("Unknown Tab.", $"Tab '{tabId}' does not exist on this Event.", "/data/attributes/tabId");
            }

            return tabId;
        }

        string? name = NormalizeOrNull(suppliedName);
        if (name is null)
        {
            throw PlacementErrors.Unprocessable("Tab is required.", "Supply either tabId or tabName.", "/data/attributes/tabId");
        }

        Tab? existing = await FindByNameAsync(dbContext.Tabs.Where(tab => tab.EventId == eventId), tab => tab.Name, name, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        int sortOrder = await dbContext.Tabs.AsNoTracking().CountAsync(tab => tab.EventId == eventId, cancellationToken);
        var created = Tab.Create(eventId, name, sortOrder);
        dbContext.Tabs.Add(created);
        return created.Id;
    }

    /// <summary>Resolves the optional Sub Tab, scoped to an already-resolved Tab.</summary>
    /// <param name="eventId">The Event the Sub Tab belongs to.</param>
    /// <param name="tabId">The resolved parent Tab.</param>
    /// <param name="suppliedId">An existing Sub Tab's id, which wins over <paramref name="suppliedName"/>.</param>
    /// <param name="suppliedName">A Sub Tab's name, matched case-insensitively.</param>
    /// <param name="cancellationToken">Propagated to the underlying queries.</param>
    /// <returns>The existing or newly tracked Sub Tab's id, or <see langword="null"/> if neither was supplied.</returns>
    public async Task<Guid?> ResolveSubTabAsync(
        Guid eventId, Guid tabId, Guid? suppliedId, string? suppliedName, CancellationToken cancellationToken)
    {
        if (suppliedId is { } subTabId)
        {
            bool belongsToTab = await dbContext.SubTabs.AsNoTracking()
                .AnyAsync(subTab => subTab.Id == subTabId && subTab.TabId == tabId, cancellationToken);

            if (!belongsToTab)
            {
                throw PlacementErrors.Unprocessable("Unknown Sub Tab.", $"Sub Tab '{subTabId}' is not under this Placement's Tab.", "/data/attributes/subTabId");
            }

            return subTabId;
        }

        string? name = NormalizeOrNull(suppliedName);
        if (name is null)
        {
            return null;
        }

        SubTab? existing = await FindByNameAsync(dbContext.SubTabs.Where(subTab => subTab.TabId == tabId), subTab => subTab.Name, name, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        int sortOrder = await dbContext.SubTabs.AsNoTracking().CountAsync(subTab => subTab.TabId == tabId, cancellationToken);
        var created = SubTab.Create(eventId, tabId, name, sortOrder);
        dbContext.SubTabs.Add(created);
        return created.Id;
    }

    /// <summary>Finds the Tier in <paramref name="scope"/> whose name matches, ignoring case.</summary>
    /// <remarks>Case-insensitive via <c>ToUpper()</c> translated into the query, matching <see cref="FacilityTypeResourceDefinition.CheckForConflictsAsync"/>'s portable technique rather than relying on column collation (ADR-0014: SQLite defaults to case-sensitive, unlike SQL Server).</remarks>
    /// <typeparam name="TTier">The Tier level.</typeparam>
    /// <param name="scope">The sibling Tiers to search.</param>
    /// <param name="nameSelector">Selects the Tier's name.</param>
    /// <param name="name">The already-trimmed name to match.</param>
    /// <param name="cancellationToken">Propagated to the underlying query.</param>
    /// <returns>The untracked match, or <see langword="null"/>.</returns>
    public static async Task<TTier?> FindByNameAsync<TTier>(
        IQueryable<TTier> scope, Expression<Func<TTier, string>> nameSelector, string name, CancellationToken cancellationToken)
        where TTier : class
    {
        var parameter = nameSelector.Parameters[0];
        var upperCall = Expression.Call(nameSelector.Body, nameof(string.ToUpper), null);
        var predicate = Expression.Lambda<Func<TTier, bool>>(
            Expression.Equal(upperCall, Expression.Constant(name.ToUpperInvariant())), parameter);

        return await scope.AsNoTracking().FirstOrDefaultAsync(predicate, cancellationToken);
    }

    /// <summary>The trimmed value in its original casing, or <see langword="null"/> if it is blank.</summary>
    /// <param name="value">The raw name.</param>
    /// <returns>The normalized name.</returns>
    public static string? NormalizeOrNull(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
