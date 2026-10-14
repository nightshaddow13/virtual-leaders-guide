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
/// (ADR-0047, currently a structural no-op - see <see cref="ValidateNoInfoPageConflictAsync"/>), checking the
/// path-uniqueness rule (ADR-0046), and assigning an initial <see cref="ActivityPlacement.SortOrder"/>. Every
/// resolve-or-create step adds to the same <see cref="VirtualLeadersGuideDbContext"/> change tracker without
/// calling <c>SaveChangesAsync</c> itself - JsonApiDotNetCore's own repository persists everything this method
/// touched, together with the Placement itself, in the one <c>SaveChangesAsync</c> call it makes after this
/// method returns, so a new Tab and its first Placement land in the same transaction or not at all.
/// </remarks>
public sealed class ActivityPlacementResourceDefinition : JsonApiResourceDefinition<ActivityPlacement, Guid>
{
    /// <remarks>See <see cref="ActivityResourceDefinition.NoEventSentinel"/> for why this sentinel is safe.</remarks>
    private static readonly Guid NoEventSentinel = Guid.Empty;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;

    /// <summary>Constructs the definition with the services it needs to authorize and resolve Placement writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> for <see cref="CurrentPolicy"/>.</param>
    /// <param name="dbContext">Backs every resolve-or-create lookup below, and <see cref="StoredEventIdAsync"/>'s re-read.</param>
    public ActivityPlacementResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
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

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
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
        Guid tabId = await ResolveTabAsync(resource, eventId, cancellationToken);
        Guid? subTabId = await ResolveSubTabAsync(resource, eventId, tabId, cancellationToken);
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
    /// Resolves <see cref="ActivityPlacement.TabId"/>/<see cref="ActivityPlacement.TabName"/> - Tab is
    /// required on every Placement (the ticket's own acceptance criteria), so exactly one of the two must
    /// be supplied.
    /// </summary>
    private async Task<Guid> ResolveTabAsync(ActivityPlacement resource, Guid eventId, CancellationToken cancellationToken)
    {
        if (resource.TabId is { } tabId)
        {
            bool belongsToEvent = await _dbContext.Tabs.AsNoTracking()
                .AnyAsync(tab => tab.Id == tabId && tab.EventId == eventId, cancellationToken);

            if (!belongsToEvent)
            {
                throw UnprocessableEntity("Unknown Tab.", $"Tab '{tabId}' does not exist on this Event.", "/data/attributes/tabId");
            }

            return tabId;
        }

        string? name = NormalizeOrNull(resource.TabName);
        if (name is null)
        {
            throw UnprocessableEntity("Tab is required.", "Supply either tabId or tabName.", "/data/attributes/tabId");
        }

        Tab? existing = await FindByNameAsync(_dbContext.Tabs.Where(tab => tab.EventId == eventId), tab => tab.Name, name.ToUpperInvariant(), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        int sortOrder = await _dbContext.Tabs.AsNoTracking().CountAsync(tab => tab.EventId == eventId, cancellationToken);
        var created = Tab.Create(eventId, name, sortOrder);
        _dbContext.Tabs.Add(created);
        return created.Id;
    }

    /// <summary>Resolves <see cref="ActivityPlacement.SubTabId"/>/<see cref="ActivityPlacement.SubTabName"/> - optional, scoped to the already-resolved <paramref name="tabId"/>.</summary>
    private async Task<Guid?> ResolveSubTabAsync(ActivityPlacement resource, Guid eventId, Guid tabId, CancellationToken cancellationToken)
    {
        if (resource.SubTabId is { } subTabId)
        {
            bool belongsToTab = await _dbContext.SubTabs.AsNoTracking()
                .AnyAsync(subTab => subTab.Id == subTabId && subTab.TabId == tabId, cancellationToken);

            if (!belongsToTab)
            {
                throw UnprocessableEntity("Unknown Sub Tab.", $"Sub Tab '{subTabId}' is not under this Placement's Tab.", "/data/attributes/subTabId");
            }

            return subTabId;
        }

        string? name = NormalizeOrNull(resource.SubTabName);
        if (name is null)
        {
            return null;
        }

        SubTab? existing = await FindByNameAsync(_dbContext.SubTabs.Where(subTab => subTab.TabId == tabId), subTab => subTab.Name, name.ToUpperInvariant(), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        int sortOrder = await _dbContext.SubTabs.AsNoTracking().CountAsync(subTab => subTab.TabId == tabId, cancellationToken);
        var created = SubTab.Create(eventId, tabId, name, sortOrder);
        _dbContext.SubTabs.Add(created);
        return created.Id;
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
                throw UnprocessableEntity("Unknown Section.", $"Section '{sectionId}' does not belong under this Placement's resolved path.", "/data/attributes/sectionId");
            }

            return sectionId;
        }

        string? name = NormalizeOrNull(resource.SectionName);
        if (name is null)
        {
            return null;
        }

        IQueryable<Section> siblings = subTabId is { } scopedSubTab
            ? _dbContext.Sections.Where(section => section.ParentSubTabId == scopedSubTab)
            : _dbContext.Sections.Where(section => section.ParentTabId == tabId);

        Section? existing = await FindByNameAsync(siblings, section => section.Name, name.ToUpperInvariant(), cancellationToken);
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
        bool wantsSubSection = resource.SubSectionId is not null || NormalizeOrNull(resource.SubSectionName) is not null;
        if (!wantsSubSection)
        {
            return null;
        }

        if (sectionId is not { } resolvedSectionId)
        {
            throw UnprocessableEntity("Sub Section requires a Section.", "Supply a sectionId or sectionName on this same Placement.", "/data/attributes/subSectionId");
        }

        if (resource.SubSectionId is { } subSectionId)
        {
            bool belongsToSection = await _dbContext.SubSections.AsNoTracking()
                .AnyAsync(subSection => subSection.Id == subSectionId && subSection.SectionId == resolvedSectionId, cancellationToken);

            if (!belongsToSection)
            {
                throw UnprocessableEntity("Unknown Sub Section.", $"Sub Section '{subSectionId}' is not under this Placement's Section.", "/data/attributes/subSectionId");
            }

            return subSectionId;
        }

        string name = NormalizeOrNull(resource.SubSectionName)!;
        SubSection? existing = await FindByNameAsync(
            _dbContext.SubSections.Where(subSection => subSection.SectionId == resolvedSectionId), subSection => subSection.Name, name.ToUpperInvariant(), cancellationToken);
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

    /// <remarks>
    /// ADR-0047's Tab/Sub-Tab InfoPage-exclusivity rule has nothing to check against yet - InfoPage's own
    /// Placement table doesn't exist in code (P5-18/19/20/21 all closed pre-four-tier-model, see this
    /// story's plan). Kept as its own named, documented no-op rather than omitted outright, so a future
    /// InfoPage Placement story has one obvious place to add the real check instead of rediscovering that
    /// this rule was never wired in.
    /// </remarks>
    private static Task ValidateNoInfoPageConflictAsync(Guid tabId, Guid? subTabId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

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

        throw new JsonApiException(new ErrorObject(HttpStatusCode.Conflict)
        {
            Title = "Resource conflict.",
            Detail = "This Activity is already placed at this exact path.",
            Source = new ErrorSource { Pointer = "/data" }
        });
    }

    /// <remarks>Case-insensitive via <c>ToUpper()</c> translated into the query, matching <see cref="FacilityTypeResourceDefinition.CheckForConflictsAsync"/>'s portable technique rather than relying on column collation (ADR-0014: SQLite defaults to case-sensitive, unlike SQL Server).</remarks>
    private static async Task<TTier?> FindByNameAsync<TTier>(
        IQueryable<TTier> scope, System.Linq.Expressions.Expression<Func<TTier, string>> nameSelector, string normalizedName, CancellationToken cancellationToken)
        where TTier : class
    {
        var parameter = nameSelector.Parameters[0];
        var upperCall = System.Linq.Expressions.Expression.Call(nameSelector.Body, nameof(string.ToUpper), null);
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<TTier, bool>>(
            System.Linq.Expressions.Expression.Equal(upperCall, System.Linq.Expressions.Expression.Constant(normalizedName)), parameter);

        return await scope.AsNoTracking().FirstOrDefaultAsync(predicate, cancellationToken);
    }

    /// <remarks>Returns the trimmed value in its original casing - for the stored Name, not the comparison. Comparison callers uppercase it themselves via <see cref="FindByNameAsync{TTier}"/>'s own normalization.</remarks>
    private static string? NormalizeOrNull(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static JsonApiException UnprocessableEntity(string title, string detail, string pointer) =>
        new(new ErrorObject(HttpStatusCode.UnprocessableEntity)
        {
            Title = title,
            Detail = detail,
            Source = new ErrorSource { Pointer = pointer }
        });

    private ActivityPlacementAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "ActivityPlacementResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException() =>
        JsonApiResourceDefinitionHelpers.ForbiddenException("You do not have permission to access this Placement.");
}
