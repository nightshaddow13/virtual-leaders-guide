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
/// Enforces Admin/Director scoping on <c>/api/activities</c> (P5-6, #87): an <c>Admin</c> gets full CRUD over
/// every Activity; a <c>Director</c> gets full CRUD too, but only over Activities on Events in
/// <see cref="ActivityAccessPolicy.AssignedEventIds"/> - unlike <c>/api/events</c>, where a Director's write
/// is Admin-only regardless of assignment (ADR-0069 records why Activity authoring diverges, the same call
/// ADR-0059 already made for <see cref="InfoPage"/>).
/// </summary>
/// <remarks>
/// Authorization lives here, the same JsonApiDotNetCore extension point <see cref="InfoPageResourceDefinition"/>
/// uses and for the same reason (ADR-0031). The collection-vs-single-resource asymmetry that ADR describes
/// also applies here: <see cref="OnApplyFilter"/> silently narrows a collection request to only visible
/// Activities, while a single-resource request outside the caller's access throws 403. Not ADR-0033's
/// always-empty-set rule - a Director's Activity visibility is sometimes non-empty, the same "ordinary
/// narrowing" case ADR-0031 already describes for Event, not the all-or-nothing case <c>UserRoleResourceDefinition</c>
/// answers.
/// </remarks>
public sealed class ActivityResourceDefinition : JsonApiResourceDefinition<Activity, Guid>
{
    /// <remarks>
    /// The <see cref="Activity.EventId"/>-equals-<see cref="Guid.Empty"/> sentinel <see cref="OnApplyFilter"/>
    /// and <see cref="OnWritingAsync"/> use to mean "no Event to check against" - safe because
    /// <see cref="Event.Create"/> always assigns a fresh <see cref="Guid.NewGuid"/> and
    /// <see cref="Activity.EventId"/> is a real foreign key, so no real row can ever resolve to this value.
    /// It's also what a nonexistent Activity id resolves to, which is why a non-Admin probing one gets 403
    /// rather than a distinguishing 404 (ADR-0031's posture, applied here the same way
    /// <see cref="InfoPageResourceDefinition"/> already applies it).
    /// </remarks>
    private static readonly Guid NoEventSentinel = Guid.Empty;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;

    /// <summary>Constructs the definition with the services it needs to authorize Activity reads and writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> for <see cref="CurrentPolicy"/>.</param>
    /// <param name="dbContext">Backs <see cref="StoredEventId"/>/<see cref="StoredEventIdAsync"/>'s re-reads and <see cref="ValidateEventExistsAsync"/>'s pre-check.</param>
    public ActivityResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A single-resource request (<see cref="IJsonApiRequest.PrimaryId"/> set) needs a synchronous lookup of
    /// the row's <see cref="Activity.EventId"/> before it can be authorized - unlike <c>Event</c>, the primary
    /// id here is the Activity's own id, not the Event id the policy actually checks against.
    /// JsonApiDotNetCore 5.11 offers no async read hook on <see cref="IResourceDefinition{TResource,TId}"/>,
    /// so this runs as a synchronous EF Core query (genuine sync I/O, not sync-over-async) rather than moving
    /// the check into <see cref="OnSerialize"/> - throwing from there would surface as a 500, since
    /// JsonApiDotNetCore's exception filter doesn't run during output formatting - or a custom
    /// <c>JsonApiResourceService</c>, which would split authorization across two types instead of keeping it
    /// in one resource definition (ADR-0031, ADR-0069). A collection request instead ANDs in the caller's
    /// assigned-Events filter, the same shape as <see cref="EventResourceDefinition.BuildAssignedEventsFilter"/>.
    /// </remarks>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        var policy = CurrentPolicy();
        if (policy.IsAdmin)
        {
            return existingFilter;
        }

        IJsonApiRequest request = JsonApiResourceDefinitionHelpers.GetRequest(_httpContextAccessor, nameof(ActivityResourceDefinition));
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

    private FilterExpression BuildAssignedEventsFilter(ActivityAccessPolicy policy)
    {
        AttrAttribute eventIdAttribute = ResourceType.GetAttributeByPropertyName(nameof(Activity.EventId));
        var eventIdChain = new ResourceFieldChainExpression(eventIdAttribute);

        IImmutableSet<LiteralConstantExpression> constants = policy.AssignedEventIds.Count == 0
            ? ImmutableHashSet.Create(new LiteralConstantExpression(NoEventSentinel))
            : policy.AssignedEventIds
                .Select(eventId => new LiteralConstantExpression(eventId))
                .ToImmutableHashSet();

        return new AnyExpression(eventIdChain, constants);
    }

    /// <remarks>
    /// A missing row resolves to <see cref="NoEventSentinel"/>, which fails every non-Admin's
    /// <see cref="ActivityAccessPolicy.CanRead"/> - the caller gets 403, and JsonApiDotNetCore's own
    /// not-found handling never runs for them. An Admin short-circuits before this is ever called, so they
    /// still get a real 404 for an unknown id.
    /// </remarks>
    private Guid StoredEventId(Guid activityId) =>
        _dbContext.Activities.AsNoTracking()
            .Where(activity => activity.Id == activityId)
            .Select(activity => activity.EventId)
            .FirstOrDefault();

    private async Task<Guid> StoredEventIdAsync(Guid activityId, CancellationToken cancellationToken) =>
        await _dbContext.Activities.AsNoTracking()
            .Where(activity => activity.Id == activityId)
            .Select(activity => activity.EventId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Authorizes first, then - <see cref="WriteOperationKind.CreateResource"/> only - pre-checks the target
    /// Event exists. <see cref="WriteOperationKind.UpdateResource"/> and <see cref="WriteOperationKind.DeleteResource"/>
    /// both authorize against the <em>stored</em> <see cref="Activity.EventId"/>, re-read via
    /// <see cref="StoredEventIdAsync"/> - required for delete, not just defensive: JsonApiDotNetCore's own
    /// contract for <see cref="WriteOperationKind.DeleteResource"/> says <paramref name="resource"/> is "an
    /// empty object with only the Id property set, because for those endpoints no resource is retrieved
    /// upfront." The same re-read pattern <c>InfoPageResourceDefinition.OnWritingAsync</c> already uses for
    /// this exact JsonApiDotNetCore behavior - a since-deleted row resolves to <see cref="NoEventSentinel"/>,
    /// which a non-Admin fails and an Admin passes through to JsonApiDotNetCore's own not-found handling.
    /// Update technically doesn't need the re-read (<see cref="Activity.EventId"/> carries no
    /// <see cref="AttrCapabilities.AllowChange"/>, so a PATCH can never overwrite it), but sharing one helper
    /// means a future capability change can't silently turn this into "trust what the caller claimed."
    /// </remarks>
    public override async Task OnWritingAsync(
        Activity resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
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
            await ValidateEventExistsAsync(resource.EventId, cancellationToken);
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
    }

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

    private ActivityAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "ActivityResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException() =>
        JsonApiResourceDefinitionHelpers.ForbiddenException("You do not have permission to access this Activity.");
}
