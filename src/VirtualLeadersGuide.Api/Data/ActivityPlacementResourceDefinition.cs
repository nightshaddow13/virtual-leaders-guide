using System.Collections.Immutable;
using System.Net;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;
using JsonApiDotNetCore.Serialization.Objects;
using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Authorization;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Enforces Admin/Director scoping on <c>/api/placements</c> (P5-11, #96) and resolves-or-creates the Tier
/// path (ADR-0072) a Placement is written against.
/// </summary>
/// <remarks>
/// Authorization mirrors <see cref="ActivityResourceDefinition"/> exactly (ADR-0069's posture extended to
/// Placement) - the same collection-vs-single-resource asymmetry, the same re-read-on-delete handling for
/// JsonApiDotNetCore's placeholder-resource contract. What's new here is everything <see cref="OnWritingAsync"/>
/// does on <see cref="WriteOperationKind.CreateResource"/> past authorization: resolving each of
/// <see cref="ActivityPlacement.TabId"/>/<see cref="ActivityPlacement.SubTabId"/>/<see cref="ActivityPlacement.SectionId"/>/
/// <see cref="ActivityPlacement.SubSectionId"/> from either a supplied id or a supplied name (creating the
/// row if the name doesn't resolve to an existing one), checking the Tab/Sub-Tab InfoPage-exclusivity rule
/// (ADR-0047, see <see cref="ValidateNoInfoPageConflictAsync"/>), checking the
/// path-uniqueness rule (ADR-0046), and assigning an initial <see cref="ActivityPlacement.SortOrder"/>. Every
/// resolve-or-create step adds to the same <see cref="VirtualLeadersGuideDbContext"/> change tracker without
/// calling <c>SaveChangesAsync</c> itself - JsonApiDotNetCore's own repository persists everything this method
/// touched, together with the Placement itself, in the one <c>SaveChangesAsync</c> call it makes after this
/// method returns, so a new Tab and its first Placement land in the same transaction or not at all.
/// On <see cref="WriteOperationKind.DeleteResource"/> (P5-12, #97) the same seam refuses to remove an
/// Activity's only Placement and has <see cref="TierReaper"/> mark every Tier the removal empties for
/// deletion, in that same single save - see <see cref="GuardLastPlacementAndReapTiersAsync"/>.
/// </remarks>
public sealed class ActivityPlacementResourceDefinition : JsonApiResourceDefinition<ActivityPlacement, Guid>
{
    /// <remarks>See <see cref="ActivityResourceDefinition.NoEventSentinel"/> for why this sentinel is safe.</remarks>
    private static readonly Guid NoEventSentinel = Guid.Empty;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;
    private readonly TierReaper _tierReaper;
    private readonly TierPathResolver _tierPathResolver;

    /// <summary>Constructs the definition with the services it needs to authorize and resolve Placement writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> for <see cref="CurrentPolicy"/>.</param>
    /// <param name="dbContext">Backs every resolve-or-create lookup below, and <see cref="StoredEventIdAsync"/>'s re-read.</param>
    /// <param name="tierReaper">Marks the Tiers a deleted Placement leaves empty for removal (P5-12, #97).</param>
    /// <param name="tierPathResolver">Resolves-or-creates a new Placement's Tab and Sub Tab.</param>
    public ActivityPlacementResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext,
        TierReaper tierReaper, TierPathResolver tierPathResolver)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
        _tierReaper = tierReaper;
        _tierPathResolver = tierPathResolver;
    }

    /// <inheritdoc/>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        var policy = CurrentPolicy();
        if (policy.IsAdmin)
        {
            return existingFilter;
        }

        IJsonApiRequest request = JsonApiResourceDefinitionHelpers.GetRequest(_httpContextAccessor, nameof(ActivityPlacementResourceDefinition));
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

    private FilterExpression BuildAssignedEventsFilter(ActivityPlacementAccessPolicy policy)
    {
        AttrAttribute eventIdAttribute = ResourceType.GetAttributeByPropertyName(nameof(ActivityPlacement.EventId));
        var eventIdChain = new ResourceFieldChainExpression(eventIdAttribute);

        IImmutableSet<LiteralConstantExpression> constants = policy.AssignedEventIds.Count == 0
            ? ImmutableHashSet.Create(new LiteralConstantExpression(NoEventSentinel))
            : policy.AssignedEventIds
                .Select(eventId => new LiteralConstantExpression(eventId))
                .ToImmutableHashSet();

        return new AnyExpression(eventIdChain, constants);
    }

    /// <remarks>
    /// Reads the denormalized <see cref="ActivityPlacement.EventId"/> column directly - unlike
    /// <see cref="ActivityResourceDefinition.StoredEventId"/>, there's no deeper relationship to resolve
    /// through, since this Placement's own row already carries its Event.
    /// </remarks>
    private Guid StoredEventId(Guid placementId) =>
        _dbContext.ActivityPlacements.AsNoTracking()
            .Where(placement => placement.Id == placementId)
            .Select(placement => placement.EventId)
            .FirstOrDefault();

    private async Task<Guid> StoredEventIdAsync(Guid placementId, CancellationToken cancellationToken) =>
        await _dbContext.ActivityPlacements.AsNoTracking()
            .Where(placement => placement.Id == placementId)
            .Select(placement => placement.EventId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc/>
    public override async Task OnWritingAsync(
        ActivityPlacement resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
    {
        var policy = CurrentPolicy();

        Guid eventId = writeOperation switch
        {
            WriteOperationKind.CreateResource => await ResolveEventIdForCreateAsync(resource, cancellationToken),
            _ => await StoredEventIdAsync(resource.Id, cancellationToken)
        };

        if (!policy.CanWrite(eventId))
        {
            throw ForbiddenException();
        }

        if (writeOperation == WriteOperationKind.CreateResource)
        {
            await ResolvePlacementAsync(resource, eventId, cancellationToken);
        }
        else if (writeOperation == WriteOperationKind.DeleteResource)
        {
            await GuardLastPlacementAndReapTiersAsync(resource.Id, cancellationToken);
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
    }

    /// <summary>
    /// Refuses to delete an Activity's only remaining Placement (ADR-0050 - deleting the Activity is the way
    /// to zero), then marks every Tier the removal empties for deletion (ADR-0046, P5-12 #97).
    /// </summary>
    /// <remarks>
    /// Loads the stored row <em>tracked</em>, on purpose: JsonApiDotNetCore hands <c>OnWritingAsync</c> an
    /// id-only placeholder on delete, then reuses whichever instance the change tracker already holds for
    /// that id. Tracking the real row (with its Tier foreign keys) is what lets EF order the Placement's
    /// delete before the Tier deletes - the foreign keys are <c>Restrict</c>, so SQL Server rejects the batch
    /// otherwise. The Tier deletes ride JsonApiDotNetCore's one <c>SaveChangesAsync</c> with the Placement's,
    /// so the removal and its reaping land together or not at all (ADR-0072).
    /// </remarks>
    private async Task GuardLastPlacementAndReapTiersAsync(Guid placementId, CancellationToken cancellationToken)
    {
        ActivityPlacement? stored = await _dbContext.ActivityPlacements
            .FirstOrDefaultAsync(placement => placement.Id == placementId, cancellationToken);
        if (stored is null)
        {
            return;
        }

        bool hasOthers = await _dbContext.ActivityPlacements.AsNoTracking()
            .AnyAsync(placement => placement.ActivityId == stored.ActivityId && placement.Id != placementId, cancellationToken);
        if (!hasOthers)
        {
            throw PlacementErrors.Unprocessable(
                "Last placement.", "An Activity needs at least one Placement; delete the Activity instead.", "/data");
        }

        await _tierReaper.ReapEmptiedTiersAsync([stored], cancellationToken);
    }

    /// <remarks>
    /// A missing <see cref="ActivityPlacement.ActivityId"/> resolves to <see cref="NoEventSentinel"/>, which
    /// fails every non-Admin's authorization below and lets an Admin through to
    /// <see cref="ValidateActivityExistsAsync"/>'s own 422 - the same "mask existence behind 403 for everyone
    /// but Admin" posture <see cref="ActivityResourceDefinition.OnWritingAsync"/> already establishes.
    /// </remarks>
    private async Task<Guid> ResolveEventIdForCreateAsync(ActivityPlacement resource, CancellationToken cancellationToken) =>
        await _dbContext.Activities.AsNoTracking()
            .Where(activity => activity.Id == resource.ActivityId)
            .Select(activity => activity.EventId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Resolves the Tier path, runs the exclusivity/uniqueness rules, and assigns <see cref="ActivityPlacement.SortOrder"/> - everything <see cref="OnWritingAsync"/> does on create once authorization has already passed.</summary>
    private async Task ResolvePlacementAsync(ActivityPlacement resource, Guid eventId, CancellationToken cancellationToken)
    {
        await ValidateActivityExistsAsync(resource.ActivityId, eventId, cancellationToken);

        resource.EventId = eventId;
        Guid tabId = await _tierPathResolver.ResolveTabAsync(eventId, resource.TabId, resource.TabName, cancellationToken);
        Guid? subTabId = await _tierPathResolver.ResolveSubTabAsync(
            eventId, tabId, resource.SubTabId, resource.SubTabName, cancellationToken);
        Guid? sectionId = await ResolveSectionAsync(resource, eventId, tabId, subTabId, cancellationToken);
        Guid? subSectionId = await ResolveSubSectionAsync(resource, eventId, sectionId, cancellationToken);

        resource.TabId = tabId;
        resource.SubTabId = subTabId;
        resource.SectionId = sectionId;
        resource.SubSectionId = subSectionId;

        await ValidateNoInfoPageConflictAsync(tabId, subTabId, cancellationToken);
        await ValidateNoDuplicatePlacementAsync(resource.ActivityId, tabId, subTabId, sectionId, subSectionId, cancellationToken);

        resource.SortOrder = await _dbContext.ActivityPlacements.AsNoTracking()
            .CountAsync(placement => placement.TabId == tabId && placement.SubTabId == subTabId
                && placement.SectionId == sectionId && placement.SubSectionId == subSectionId, cancellationToken);
    }

    /// <remarks>Runs after authorization (see <see cref="ResolveEventIdForCreateAsync"/>'s remarks) - a non-Admin never learns whether an unknown Activity exists.</remarks>
    private async Task ValidateActivityExistsAsync(Guid activityId, Guid eventId, CancellationToken cancellationToken)
    {
        bool exists = eventId != NoEventSentinel && await _dbContext.Activities.AsNoTracking()
            .AnyAsync(activity => activity.Id == activityId, cancellationToken);

        if (exists)
        {
            return;
        }

        throw new JsonApiException(new ErrorObject(HttpStatusCode.UnprocessableEntity)
        {
            Title = "Unknown Activity.",
            Detail = $"Activity '{activityId}' does not exist.",
            Source = new ErrorSource { Pointer = "/data/attributes/activityId" }
        });
    }

    /// <summary>
    /// Resolves <see cref="ActivityPlacement.SectionId"/>/<see cref="ActivityPlacement.SectionName"/> -
    /// optional. Its parent is derived, not separately supplied: the resolved <paramref name="subTabId"/>
    /// when this Placement sets one, otherwise the bare <paramref name="tabId"/> (ADR-0046).
    /// </summary>
    private async Task<Guid?> ResolveSectionAsync(
        ActivityPlacement resource, Guid eventId, Guid tabId, Guid? subTabId, CancellationToken cancellationToken)
    {
        if (resource.SectionId is { } sectionId)
        {
            Section? section = await _dbContext.Sections.AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == sectionId, cancellationToken);

            bool matchesParent = section is not null && (subTabId is { } expectedSubTab
                ? section.ParentSubTabId == expectedSubTab
                : section.ParentTabId == tabId);

            if (!matchesParent)
            {
                throw PlacementErrors.Unprocessable("Unknown Section.", $"Section '{sectionId}' does not belong under this Placement's resolved path.", "/data/attributes/sectionId");
            }

            return sectionId;
        }

        string? name = TierPathResolver.NormalizeOrNull(resource.SectionName);
        if (name is null)
        {
            return null;
        }

        IQueryable<Section> siblings = subTabId is { } scopedSubTab
            ? _dbContext.Sections.Where(section => section.ParentSubTabId == scopedSubTab)
            : _dbContext.Sections.Where(section => section.ParentTabId == tabId);

        Section? existing = await TierPathResolver.FindByNameAsync(siblings, section => section.Name, name, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        int sortOrder = await siblings.AsNoTracking().CountAsync(cancellationToken);
        Section created = subTabId is { } parentSubTab
            ? Section.CreateUnderSubTab(eventId, parentSubTab, name, sortOrder)
            : Section.CreateUnderTab(eventId, tabId, name, sortOrder);
        _dbContext.Sections.Add(created);
        return created.Id;
    }

    /// <summary>
    /// Resolves <see cref="ActivityPlacement.SubSectionId"/>/<see cref="ActivityPlacement.SubSectionName"/> -
    /// optional, but requires a Section resolved on this same Placement (<paramref name="sectionId"/>).
    /// </summary>
    private async Task<Guid?> ResolveSubSectionAsync(
        ActivityPlacement resource, Guid eventId, Guid? sectionId, CancellationToken cancellationToken)
    {
        bool wantsSubSection = resource.SubSectionId is not null || TierPathResolver.NormalizeOrNull(resource.SubSectionName) is not null;
        if (!wantsSubSection)
        {
            return null;
        }

        if (sectionId is not { } resolvedSectionId)
        {
            throw PlacementErrors.Unprocessable("Sub Section requires a Section.", "Supply a sectionId or sectionName on this same Placement.", "/data/attributes/subSectionId");
        }

        if (resource.SubSectionId is { } subSectionId)
        {
            bool belongsToSection = await _dbContext.SubSections.AsNoTracking()
                .AnyAsync(subSection => subSection.Id == subSectionId && subSection.SectionId == resolvedSectionId, cancellationToken);

            if (!belongsToSection)
            {
                throw PlacementErrors.Unprocessable("Unknown Sub Section.", $"Sub Section '{subSectionId}' is not under this Placement's Section.", "/data/attributes/subSectionId");
            }

            return subSectionId;
        }

        string name = TierPathResolver.NormalizeOrNull(resource.SubSectionName)!;
        SubSection? existing = await TierPathResolver.FindByNameAsync(
            _dbContext.SubSections.Where(subSection => subSection.SectionId == resolvedSectionId), subSection => subSection.Name, name, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        int sortOrder = await _dbContext.SubSections.AsNoTracking()
            .CountAsync(subSection => subSection.SectionId == resolvedSectionId, cancellationToken);
        var created = SubSection.Create(eventId, resolvedSectionId, name, sortOrder);
        _dbContext.SubSections.Add(created);
        return created.Id;
    }

    /// <remarks>ADR-0047: a screen is the exact (Tab, Sub Tab) pair, so a Section-level Placement counts against its Tab's (or Sub Tab's) screen.</remarks>
    private async Task ValidateNoInfoPageConflictAsync(Guid tabId, Guid? subTabId, CancellationToken cancellationToken)
    {
        if (await _dbContext.InfoPagePlacements.AsNoTracking()
                .AnyAsync(placement => placement.TabId == tabId && placement.SubTabId == subTabId, cancellationToken))
        {
            throw PlacementErrors.Conflict(PlacementErrors.SlotHasInfoPage, "This screen already shows an InfoPage.");
        }
    }

    /// <remarks>
    /// A pre-check, not a <see cref="DbUpdateException"/> catch - matching <see cref="UserRoleResourceDefinition.CheckForConflictsAsync"/>'s
    /// reasoning (ADR-0014's cross-provider error-code rule). Backstopped at the DB level by
    /// <see cref="VirtualLeadersGuideDbContext"/>'s six filtered unique indexes.
    /// </remarks>
    private async Task ValidateNoDuplicatePlacementAsync(
        Guid activityId, Guid tabId, Guid? subTabId, Guid? sectionId, Guid? subSectionId, CancellationToken cancellationToken)
    {
        bool duplicate = await _dbContext.ActivityPlacements.AsNoTracking().AnyAsync(placement =>
            placement.ActivityId == activityId && placement.TabId == tabId && placement.SubTabId == subTabId
            && placement.SectionId == sectionId && placement.SubSectionId == subSectionId, cancellationToken);

        if (!duplicate)
        {
            return;
        }

        throw PlacementErrors.Conflict(PlacementErrors.Duplicate, "This Activity is already placed at this exact path.");
    }

    private ActivityPlacementAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "ActivityPlacementResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException() =>
        JsonApiResourceDefinitionHelpers.ForbiddenException("You do not have permission to access this Placement.");
}
