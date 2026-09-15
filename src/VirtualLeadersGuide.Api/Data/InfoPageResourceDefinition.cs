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
/// Enforces Admin/Director scoping on <c>/api/infoPages</c> (P5-16, #21): an <c>Admin</c> gets full CRUD over
/// every InfoPage; a <c>Director</c> gets full CRUD too, but only over InfoPages on Events in
/// <see cref="InfoPageAccessPolicy.AssignedEventIds"/> - unlike <c>/api/events</c>, where a Director's write
/// is Admin-only regardless of assignment (ADR-0059 records why InfoPage authoring diverges).
/// </summary>
/// <remarks>
/// Authorization lives here, the same JsonApiDotNetCore extension point <see cref="EventResourceDefinition"/>
/// uses and for the same reason (ADR-0031). The collection-vs-single-resource asymmetry that ADR describes
/// also applies here: <see cref="OnApplyFilter"/> silently narrows a collection request to only visible
/// InfoPages, while a single-resource request outside the caller's access throws 403. Not ADR-0033's
/// always-empty-set rule - a Director's InfoPage visibility is sometimes non-empty, the same "ordinary
/// narrowing" case ADR-0031 already describes for Event, not the all-or-nothing case <c>UserRoleResourceDefinition</c>
/// answers.
/// </remarks>
public sealed class InfoPageResourceDefinition : JsonApiResourceDefinition<InfoPage, Guid>
{
    /// <remarks>
    /// The <see cref="Page.EventId"/>-equals-<see cref="Guid.Empty"/> sentinel <see cref="OnApplyFilter"/> and
    /// <see cref="OnWritingAsync"/> use to mean "no Event to check against" - safe because <see cref="Event.Create"/>
    /// always assigns a fresh <see cref="Guid.NewGuid"/> and <see cref="Page.EventId"/> is a real foreign key,
    /// so no real row can ever resolve to this value. It's also what a nonexistent InfoPage id resolves to,
    /// which is why a non-Admin probing one gets 403 rather than a distinguishing 404 (ADR-0031's posture,
    /// applied here the same way <see cref="EventResourceDefinition"/> already applies it to an unknown Event
    /// id).
    /// </remarks>
    private static readonly Guid NoEventSentinel = Guid.Empty;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;

    /// <summary>Constructs the definition with the services it needs to authorize InfoPage reads and writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> for <see cref="CurrentPolicy"/>.</param>
    /// <param name="dbContext">Backs <see cref="StoredEventId"/>/<see cref="StoredEventIdAsync"/>'s re-reads and <see cref="ValidateEventExistsAsync"/>'s pre-check.</param>
    public InfoPageResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A single-resource request (<see cref="IJsonApiRequest.PrimaryId"/> set) needs a synchronous lookup of
    /// the row's <see cref="Page.EventId"/> before it can be authorized - unlike <c>Event</c>, the primary id
    /// here is the InfoPage's own id, not the Event id the policy actually checks against. JsonApiDotNetCore
    /// 5.11 offers no async read hook on <see cref="IResourceDefinition{TResource,TId}"/>, so this runs as a
    /// synchronous EF Core query (genuine sync I/O, not sync-over-async) rather than moving the check into
    /// <see cref="OnSerialize"/> - throwing from there would surface as a 500, since JsonApiDotNetCore's
    /// exception filter doesn't run during output formatting - or a custom <c>JsonApiResourceService</c>,
    /// which would split authorization across two types instead of keeping it in one resource definition
    /// (ADR-0031, ADR-0059). A collection request instead ANDs in the caller's assigned-Events filter, the
    /// same shape as <see cref="EventResourceDefinition.BuildAssignedEventsFilter"/>.
    /// </remarks>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        var policy = CurrentPolicy();
        if (policy.IsAdmin)
        {
            return existingFilter;
        }

        IJsonApiRequest request = JsonApiResourceDefinitionHelpers.GetRequest(_httpContextAccessor, nameof(InfoPageResourceDefinition));
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
        AttrAttribute eventIdAttribute = ResourceType.GetAttributeByPropertyName(nameof(Page.EventId));
        var eventIdChain = new ResourceFieldChainExpression(eventIdAttribute);

        IImmutableSet<LiteralConstantExpression> constants = policy.AssignedEventIds.Count == 0
            ? ImmutableHashSet.Create(new LiteralConstantExpression(NoEventSentinel))
            : policy.AssignedEventIds
                .Select(eventId => new LiteralConstantExpression(eventId))
                .ToImmutableHashSet();

        return new AnyExpression(eventIdChain, constants);
    }

    /// <remarks>
    /// Queries <c>InfoPages</c> specifically, not the base <c>Pages</c> table - this definition governs
    /// InfoPages only, so a future second <see cref="Page"/> subtype's id must never resolve here. A missing
    /// row resolves to <see cref="NoEventSentinel"/>, which fails every non-Admin's <see cref="InfoPageAccessPolicy.CanRead"/>
    /// - the caller gets 403, and JsonApiDotNetCore's own not-found handling never runs for them. An Admin
    /// short-circuits before this is ever called, so they still get a real 404 for an unknown id.
    /// </remarks>
    private Guid StoredEventId(Guid infoPageId) =>
        _dbContext.InfoPages.AsNoTracking()
            .Where(page => page.Id == infoPageId)
            .Select(page => page.EventId)
            .FirstOrDefault();

    private async Task<Guid> StoredEventIdAsync(Guid infoPageId, CancellationToken cancellationToken) =>
        await _dbContext.InfoPages.AsNoTracking()
            .Where(page => page.Id == infoPageId)
            .Select(page => page.EventId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Authorizes first, then - <see cref="WriteOperationKind.CreateResource"/> only - fills
    /// <see cref="Page.PageTypeId"/> and pre-checks the target Event exists. <see cref="WriteOperationKind.UpdateResource"/>
    /// and <see cref="WriteOperationKind.DeleteResource"/> both authorize against the <em>stored</em>
    /// <see cref="Page.EventId"/>, re-read via <see cref="StoredEventIdAsync"/> - required for delete, not
    /// just defensive: JsonApiDotNetCore's own contract for <see cref="WriteOperationKind.DeleteResource"/>
    /// says <paramref name="resource"/> is "an empty object with only the Id property set, because for those
    /// endpoints no resource is retrieved upfront." The same re-read pattern <c>UserRoleResourceDefinition.GrantForDeleteAsync</c>
    /// already uses for this exact JsonApiDotNetCore behavior - a since-deleted row resolves to
    /// <see cref="NoEventSentinel"/>, which a non-Admin fails and an Admin passes through to JsonApiDotNetCore's
    /// own not-found handling. Update technically doesn't need the re-read (<see cref="Page.EventId"/> carries
    /// no <see cref="AttrCapabilities.AllowChange"/>, so a PATCH can never overwrite it), but sharing one
    /// helper means a future capability change can't silently turn this into "trust what the caller claimed."
    /// </remarks>
    public override async Task OnWritingAsync(
        InfoPage resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
    {
        var policy = CurrentPolicy();

        bool allowed = writeOperation switch
        {
            WriteOperationKind.CreateResource => policy.CanWrite(resource.EventId),
            WriteOperationKind.UpdateResource or WriteOperationKind.DeleteResource =>
                policy.CanWrite(await StoredEventIdAsync(resource.Id, cancellationToken)),
            _ => true
        };

        if (!allowed)
        {
            throw ForbiddenException();
        }

        if (writeOperation == WriteOperationKind.CreateResource)
        {
            FillServerGeneratedDefaults(resource);
            await ValidateEventExistsAsync(resource.EventId, cancellationToken);
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
    }

    /// <remarks>
    /// Mirrors what <see cref="InfoPage.Create"/> does for in-process callers - JsonApiDotNetCore constructs
    /// the resource through its own resource factory and never routes a POST body through that factory (see
    /// <see cref="InfoPage.Create"/>'s remarks). Unconditional, not "if unset": <see cref="Page.PageTypeId"/>
    /// carries no <see cref="AttrAttribute"/> at all, so a POST body can never populate it in the first place.
    /// </remarks>
    private static void FillServerGeneratedDefaults(InfoPage resource) =>
        resource.PageTypeId = PageTypeIds.InfoPage;

    /// <remarks>
    /// Runs after authorization, so a non-Admin never learns whether an unknown Event id exists - they get
    /// 403 either way, from the check above. This mainly protects the Admin path from an FK-violation 500.
    /// </remarks>
    private async Task ValidateEventExistsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        bool exists = await _dbContext.Events.AsNoTracking()
            .AnyAsync(e => e.Id == eventId, cancellationToken);

        if (exists)
        {
            return;
        }

        throw new JsonApiException(new ErrorObject(HttpStatusCode.UnprocessableEntity)
        {
            Title = "Unknown Event.",
            Detail = $"Event '{eventId}' does not exist.",
            Source = new ErrorSource { Pointer = "/data/attributes/eventId" }
        });
    }

    private InfoPageAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "InfoPageResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException() =>
        JsonApiResourceDefinitionHelpers.ForbiddenException("You do not have permission to access this InfoPage.");
}
