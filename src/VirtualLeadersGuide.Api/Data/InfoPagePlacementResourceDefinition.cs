using System.Collections.Immutable;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;
using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Authorization;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Enforces Admin/Director scoping on <c>/api/infoPagePlacements</c> (P5-24, #208), resolves-or-creates the
/// Tab/Sub Tab path (ADR-0072) and keeps each screen exclusive (ADR-0047).
/// </summary>
/// <remarks>
/// Scoping mirrors <see cref="ActivityPlacementResourceDefinition"/> and uses
/// <see cref="InfoPageAccessPolicy"/> as is: authority over a Placement is exactly authority over its InfoPage.
/// A "screen" is the exact (Tab, Sub Tab) pair, so a bare Tab and each of its Sub Tabs are checked separately.
/// Every write rides JsonApiDotNetCore's one <c>SaveChangesAsync</c>, so new Tiers, the Placement and any reaped
/// Tiers land together or not at all.
/// </remarks>
public sealed class InfoPagePlacementResourceDefinition : JsonApiResourceDefinition<InfoPagePlacement, Guid>
{
    /// <remarks>See <see cref="ActivityResourceDefinition.NoEventSentinel"/> for why this sentinel is safe.</remarks>
    private static readonly Guid NoEventSentinel = Guid.Empty;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;
    private readonly TierPathResolver _tierPathResolver;
    private readonly TierReaper _tierReaper;
    private readonly ITargetedFields _targetedFields;

    /// <summary>Constructs the definition with the services it needs to authorize and resolve writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's user.</param>
    /// <param name="dbContext">Backs every lookup below.</param>
    /// <param name="tierPathResolver">Resolves-or-creates the Tab and Sub Tab.</param>
    /// <param name="tierReaper">Marks the Tiers a move or removal leaves empty.</param>
    /// <param name="targetedFields">Tells a move which attributes the PATCH actually named.</param>
    public InfoPagePlacementResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext,
        TierPathResolver tierPathResolver, TierReaper tierReaper, ITargetedFields targetedFields)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
        _tierPathResolver = tierPathResolver;
        _tierReaper = tierReaper;
        _targetedFields = targetedFields;
    }

    /// <inheritdoc/>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        var policy = CurrentPolicy();
        if (policy.IsAdmin)
        {
            return existingFilter;
        }

        IJsonApiRequest request = JsonApiResourceDefinitionHelpers.GetRequest(_httpContextAccessor, nameof(InfoPagePlacementResourceDefinition));
        if (request.PrimaryId is not null)
        {
            if (!policy.CanRead(StoredEventId(Guid.Parse(request.PrimaryId))))
            {
                throw ForbiddenException();
            }

            return existingFilter;
        }

        return JsonApiResourceDefinitionHelpers.And(existingFilter, BuildAssignedEventsFilter(policy));
    }

    private FilterExpression BuildAssignedEventsFilter(InfoPageAccessPolicy policy)
    {
        AttrAttribute eventIdAttribute = ResourceType.GetAttributeByPropertyName(nameof(InfoPagePlacement.EventId));
        var eventIdChain = new ResourceFieldChainExpression(eventIdAttribute);

        IImmutableSet<LiteralConstantExpression> constants = policy.AssignedEventIds.Count == 0
            ? ImmutableHashSet.Create(new LiteralConstantExpression(NoEventSentinel))
            : policy.AssignedEventIds
                .Select(eventId => new LiteralConstantExpression(eventId))
                .ToImmutableHashSet();

        return new AnyExpression(eventIdChain, constants);
    }

    private Guid StoredEventId(Guid placementId) =>
        _dbContext.InfoPagePlacements.AsNoTracking()
            .Where(placement => placement.Id == placementId)
            .Select(placement => placement.EventId)
            .FirstOrDefault();

    /// <inheritdoc/>
    public override async Task OnWritingAsync(
        InfoPagePlacement resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
    {
        if (writeOperation == WriteOperationKind.CreateResource)
        {
            await PlaceAsync(resource, cancellationToken);
        }
        else if (writeOperation == WriteOperationKind.UpdateResource)
        {
            await MoveAsync(resource, cancellationToken);
        }
        else if (writeOperation == WriteOperationKind.DeleteResource)
        {
            await RemoveAsync(resource.Id, cancellationToken);
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
    }

    /// <remarks>
    /// A missing InfoPage resolves to <see cref="NoEventSentinel"/>: a non-Admin gets 403 and learns nothing, an
    /// Admin gets the 422.
    /// </remarks>
    private async Task PlaceAsync(InfoPagePlacement resource, CancellationToken cancellationToken)
    {
        Guid eventId = await _dbContext.InfoPages.AsNoTracking()
            .Where(page => page.Id == resource.InfoPageId)
            .Select(page => page.EventId)
            .FirstOrDefaultAsync(cancellationToken);
        Authorize(eventId);

        if (eventId == NoEventSentinel)
        {
            throw PlacementErrors.Unprocessable(
                "Unknown InfoPage.", $"InfoPage '{resource.InfoPageId}' does not exist.", "/data/attributes/infoPageId");
        }

        if (await _dbContext.InfoPagePlacements.AsNoTracking()
                .AnyAsync(placement => placement.InfoPageId == resource.InfoPageId, cancellationToken))
        {
            throw PlacementErrors.Conflict(PlacementErrors.InfoPageAlreadyPlaced, "This InfoPage is already placed.");
        }

        Guid tabId = await _tierPathResolver.ResolveTabAsync(eventId, resource.TabId, resource.TabName, cancellationToken);
        Guid? subTabId = await _tierPathResolver.ResolveSubTabAsync(
            eventId, tabId, resource.SubTabId, resource.SubTabName, cancellationToken);

        await ValidateScreenIsFreeAsync(resource.Id, tabId, subTabId, cancellationToken);

        resource.EventId = eventId;
        resource.TabId = tabId;
        resource.SubTabId = subTabId;
    }

    /// <remarks>
    /// <paramref name="resource"/> is the tracked stored row with the request's attributes already copied over it,
    /// so an untargeted <see cref="InfoPagePlacement.TabId"/> still holds the <em>old</em> value - and the
    /// resolver prefers an id over a name. Only targeted values are passed on. A PATCH that names any of the four
    /// path attributes replaces the whole path (no Sub Tab named means none); one that names none is a no-op.
    /// </remarks>
    private async Task MoveAsync(InfoPagePlacement resource, CancellationToken cancellationToken)
    {
        InfoPagePlacement? stored = await _dbContext.InfoPagePlacements.AsNoTracking()
            .FirstOrDefaultAsync(placement => placement.Id == resource.Id, cancellationToken);
        Authorize(stored?.EventId ?? NoEventSentinel);

        bool targetsTabId = IsTargeted(nameof(InfoPagePlacement.TabId));
        bool targetsTabName = IsTargeted(nameof(InfoPagePlacement.TabName));
        bool targetsSubTabId = IsTargeted(nameof(InfoPagePlacement.SubTabId));
        bool targetsSubTabName = IsTargeted(nameof(InfoPagePlacement.SubTabName));
        if (stored is null || !(targetsTabId || targetsTabName || targetsSubTabId || targetsSubTabName))
        {
            return;
        }

        Guid tabId = await _tierPathResolver.ResolveTabAsync(
            stored.EventId, targetsTabId ? resource.TabId : null, targetsTabName ? resource.TabName : null, cancellationToken);
        Guid? subTabId = await _tierPathResolver.ResolveSubTabAsync(
            stored.EventId, tabId, targetsSubTabId ? resource.SubTabId : null,
            targetsSubTabName ? resource.SubTabName : null, cancellationToken);

        resource.TabId = tabId;
        resource.SubTabId = subTabId;
        if (tabId == stored.TabId && subTabId == stored.SubTabId)
        {
            return;
        }

        await ValidateScreenIsFreeAsync(stored.Id, tabId, subTabId, cancellationToken);

        HashSet<Guid> keep = subTabId is { } keptSubTab ? [tabId, keptSubTab] : [tabId];
        await _tierReaper.ReapEmptiedTiersAsync([stored], keep, cancellationToken);
    }

    /// <remarks>Loads the stored row tracked for the same reason as <c>ActivityPlacementResourceDefinition</c>'s delete: EF must order it before the <c>Restrict</c>ed Tier deletes.</remarks>
    private async Task RemoveAsync(Guid placementId, CancellationToken cancellationToken)
    {
        InfoPagePlacement? stored = await _dbContext.InfoPagePlacements
            .FirstOrDefaultAsync(placement => placement.Id == placementId, cancellationToken);
        Authorize(stored?.EventId ?? NoEventSentinel);

        if (stored is not null)
        {
            await _tierReaper.ReapEmptiedTiersAsync([stored], keep: null, cancellationToken);
        }
    }

    private async Task ValidateScreenIsFreeAsync(Guid placementId, Guid tabId, Guid? subTabId, CancellationToken cancellationToken)
    {
        if (await _dbContext.ActivityPlacements.AsNoTracking()
                .AnyAsync(placement => placement.TabId == tabId && placement.SubTabId == subTabId, cancellationToken))
        {
            throw PlacementErrors.Conflict(PlacementErrors.SlotHasActivities, "This screen already shows Activities.");
        }

        if (await _dbContext.InfoPagePlacements.AsNoTracking().AnyAsync(
                placement => placement.Id != placementId && placement.TabId == tabId && placement.SubTabId == subTabId,
                cancellationToken))
        {
            throw PlacementErrors.Conflict(PlacementErrors.SlotHasOtherInfoPage, "This screen already shows another InfoPage.");
        }
    }

    private bool IsTargeted(string propertyName) =>
        _targetedFields.Attributes.Contains(ResourceType.GetAttributeByPropertyName(propertyName));

    private void Authorize(Guid eventId)
    {
        if (!CurrentPolicy().CanWrite(eventId))
        {
            throw ForbiddenException();
        }
    }

    private InfoPageAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "InfoPagePlacementResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException() =>
        JsonApiResourceDefinitionHelpers.ForbiddenException("You do not have permission to access this Placement.");
}
