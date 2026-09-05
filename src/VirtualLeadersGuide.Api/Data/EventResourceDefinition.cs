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
/// Enforces Admin/Director scoping on <c>/api/events</c> (P2-7, #16; narrowed by P2-9, #18): an
/// <c>Admin</c> gets full CRUD over every Event; a <c>Director</c> gets read-only access to the Events in
/// their <see cref="EventAccessPolicy.AssignedEventIds"/>, and never create, update, or delete (ADR-0031).
/// </summary>
/// <remarks>
/// Authorization lives here - a JsonApiDotNetCore extension point - rather than a hand-written controller or
/// ASP.NET Core middleware; see ADR-0031 for why that still satisfies ADR-0004's "zero hand-written
/// controllers" framing, and for the collection-vs-single-resource asymmetry this type deliberately
/// produces: <see cref="OnApplyFilter"/> silently narrows a collection request to only visible Events (no
/// error, possibly an empty page), while a single-resource request for an Event outside the caller's access
/// throws 403 - confirming the Event's existence rather than returning 404. Both are considered, not
/// accidental (ADR-0031). Since P2-20 (#115), <see cref="OnApplyFilter"/> also owns the Status-lifecycle
/// concerns ADR-0044/ADR-0053 describe (the default-list hide, client status-filter rewriting) - see its own
/// remarks for the current three-job shape, not just Director scoping.
/// </remarks>
public sealed class EventResourceDefinition : JsonApiResourceDefinition<Event, Guid>
{
    /// <remarks>
    /// The <see cref="Event.Id"/>-equals-<see cref="Guid.Empty"/> sentinel <see cref="OnApplyFilter"/> uses
    /// to mean "no Events visible" - safe because <see cref="Event.Create"/> always assigns a fresh
    /// <see cref="Guid.NewGuid"/>, so no real Event row can ever have this id.
    /// </remarks>
    private static readonly Guid NoEventsSentinel = Guid.Empty;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;
    private readonly ITargetedFields _targetedFields;
    private readonly TimeProvider _timeProvider;

    /// <summary>Constructs the definition with the services it needs to authorize and default Event writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">
    /// Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> (for
    /// <see cref="EventAccessPolicy"/>) and <see cref="IJsonApiRequest"/> - see <see cref="CurrentPolicy"/> and
    /// <see cref="GetRequest"/>.
    /// </param>
    /// <param name="dbContext">Backs <see cref="CheckForConflictsAsync"/>'s Name/Slug uniqueness pre-check and <see cref="ValidateStatusTransitionAsync"/>'s pre-PATCH lookup.</param>
    /// <param name="targetedFields">Tells <see cref="ValidateStatusTransitionAsync"/> whether a PATCH actually named <see cref="Event.Status"/>, so an ordinary Save changes skips the lookup entirely.</param>
    /// <param name="timeProvider">The single clock source for every "is this Live row actually Past" check in this type - see <see cref="EffectiveStatus"/>.</param>
    public EventResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext,
        ITargetedFields targetedFields, TimeProvider timeProvider)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
        _targetedFields = targetedFields;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Three jobs in one pass, in order. (1) A single-resource request (<see cref="IJsonApiRequest.PrimaryId"/>
    /// set) is authorized directly - 403 when a non-Admin can't read it - and never status-narrowed, so a
    /// <see cref="EventStatus.Past"/>/<see cref="EventStatus.Cancelled"/> Event stays reachable by direct URL,
    /// per the AC. (2) A client-supplied filter naming <see cref="Event.Status"/> is rewritten into its
    /// effective form via <see cref="EventStatusFilterRewriter"/>, since <see cref="EventStatus.Past"/> is
    /// never a stored value. (3) When no filter named Status at all, the default read-only collection view is
    /// narrowed to <see cref="EventStatusFilterRewriter.DefaultVisibleStatuses"/>. Gated on
    /// <see cref="IJsonApiRequest.IsReadOnly"/>, not just <c>PrimaryId is null</c> - JsonApiDotNetCore
    /// re-reads a just-written resource to build a write response body (e.g. the 201 after a <c>POST</c>, or
    /// re-fetching after a status-changing <c>PATCH</c>) via a query that also has no <c>PrimaryId</c>, and
    /// that internal read must never be narrowed by the default hide - narrowing it could make the
    /// response-building query find nothing for a resource a write just moved out of the default view (e.g.
    /// a Cancel action). Finally ANDs in the Director scope filter for a non-Admin, unchanged from before this
    /// story - an Admin's request now also goes through (2)/(3), unlike before P2-20, since the default-hide
    /// rule applies to every caller's dashboard, not just a Director's.
    /// </remarks>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        var policy = CurrentPolicy();
        IJsonApiRequest request = GetRequest();

        if (request.PrimaryId is not null)
        {
            if (!policy.IsAdmin && !policy.CanRead(Guid.Parse(request.PrimaryId)))
            {
                throw ForbiddenException();
            }

            return existingFilter;
        }

        ResourceFieldChainExpression statusChain = StatusChain();
        ResourceFieldChainExpression endsAtChain = EndsAtChain();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool canCompareEndsAt = CanCompareEndsAt();

        var rewriter = new EventStatusFilterRewriter(statusChain, endsAtChain, now, canCompareEndsAt);
        FilterExpression? filter = existingFilter is null
            ? null
            : rewriter.Visit(existingFilter, null) as FilterExpression;

        if (request.IsReadOnly && !rewriter.ClientNamedStatus)
        {
            filter = And(filter,
                EventStatusFilterRewriter.DefaultVisibleStatuses(statusChain, endsAtChain, now, canCompareEndsAt));
        }

        if (!policy.IsAdmin)
        {
            filter = And(filter, BuildAssignedEventsFilter(policy));
        }

        return filter;
    }

    private static FilterExpression? And(FilterExpression? left, FilterExpression right) =>
        left is null ? right : new LogicalExpression(LogicalOperator.And, left, right);

    private ResourceFieldChainExpression StatusChain() =>
        new(ResourceType.GetAttributeByPropertyName(nameof(Event.Status)));

    private ResourceFieldChainExpression EndsAtChain() =>
        new(ResourceType.GetAttributeByPropertyName(nameof(Event.EndsAt)));

    /// <remarks>
    /// EF Core's SQLite provider (used by the Api test suite, ADR-0014) has never translated
    /// <c>&gt;</c>/<c>&lt;</c>/<c>&gt;=</c>/<c>&lt;=</c> on <see cref="DateTimeOffset"/>, only <c>==</c>/<c>!=</c>
    /// - a permanent provider limitation, not a bug (see <see cref="EventStatusFilterRewriter.LiveNotElapsed"/>'s
    /// remarks for the citation). Production (SQL Server) has no such gap. Checked via
    /// <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.ProviderName"/> - a plain string
    /// compare rather than the <c>Database.IsSqlite()</c> extension, since that extension lives in the
    /// <c>Microsoft.EntityFrameworkCore.Sqlite</c> package, which this - the production - project deliberately
    /// never references (it only ever talks to SQL Server; SQLite is test-only). See ADR-0053.
    /// </remarks>
    private bool CanCompareEndsAt() => _dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite";

    /// <inheritdoc/>
    /// <remarks>
    /// Authorizes the write per <see cref="EventAccessPolicy"/> first, then - <see cref="WriteOperationKind.CreateResource"/>
    /// only - generates <see cref="Event.Slug"/>/<see cref="Event.Passcode"/> server-side, the same way
    /// <see cref="Event.Create"/> does for in-process callers (JsonApiDotNetCore constructs <see cref="Event"/>
    /// through its own resource factory, never through <see cref="Event.Create"/>) - see
    /// <see cref="FillServerGeneratedDefaults"/> for why this is unconditional rather than "if blank". Finally
    /// pre-checks the <see cref="Event.Name"/>/<see cref="Event.Slug"/> unique indexes and throws a 409 naming
    /// whichever collided - see <see cref="CheckForConflictsAsync"/> for why this is a pre-check rather than
    /// catching the eventual <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>. Also validates
    /// <see cref="Event.StartsAt"/>/<see cref="Event.EndsAt"/>'s ordering rules (<see cref="ValidateDateRange"/>)
    /// and, on an update, <see cref="Event.Status"/>'s transition (<see cref="ValidateStatusTransitionAsync"/>) -
    /// both throw a 422 naming whichever attribute is wrong. A <c>POST</c> naming <see cref="Event.Status"/>
    /// needs no code here at all: <see cref="Event.Status"/> carries no <see cref="AttrCapabilities.AllowCreate"/>,
    /// so JsonApiDotNetCore itself already rejects it with 422 at <c>/data/attributes/status</c> before this
    /// method ever runs.
    /// </remarks>
    public override async Task OnWritingAsync(
        Event resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
    {
        var policy = CurrentPolicy();
        bool allowed = writeOperation switch
        {
            WriteOperationKind.CreateResource => policy.CanCreate,
            WriteOperationKind.UpdateResource => policy.CanUpdate(resource.Id),
            WriteOperationKind.DeleteResource => policy.CanDelete,
            _ => true
        };

        if (!allowed)
        {
            throw ForbiddenException();
        }

        if (writeOperation == WriteOperationKind.CreateResource)
        {
            FillServerGeneratedDefaults(resource);
        }

        if (writeOperation is WriteOperationKind.CreateResource or WriteOperationKind.UpdateResource)
        {
            await CheckForConflictsAsync(resource, cancellationToken);
            ValidateDateRange(resource);
            await ValidateStatusTransitionAsync(resource, writeOperation, cancellationToken);
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Produces <see cref="EventStatus.Past"/> for a read - the only place it's ever produced, since it's
    /// never stored (<see cref="Event.Status"/>'s remarks; ADR-0053). Fires for every primary and included
    /// resource, on both collection and single reads. Safe to mutate <paramref name="resource"/> here: reads
    /// run no-tracking, and on a write <c>SaveChanges</c> has already completed before serialization runs -
    /// nothing this method does can reach the database. <c>CK_Events_Status_Allowed</c>
    /// (<see cref="VirtualLeadersGuideDbContext"/>) is the belt-and-braces backstop regardless.
    /// </remarks>
    public override void OnSerialize(Event resource)
    {
        resource.Status = EffectiveStatus(resource.Status, resource.EndsAt, _timeProvider.GetUtcNow());
    }

    /// <remarks>
    /// The one place "is this Live row actually Past" is computed - shared by <see cref="OnSerialize"/>,
    /// <see cref="ValidateStatusTransitionAsync"/>, and <see cref="CheckForConflictsAsync"/>'s Name check, so
    /// the rule can't drift between the three. <paramref name="endsAt"/> is deliberately <see langword="null"/>-safe:
    /// a <see cref="EventStatus.Live"/> Event with no end date is never Past (CONTEXT.md's Starts at / Ends at
    /// entry - an unset date isn't an elapsed one).
    /// </remarks>
    private static EventStatus EffectiveStatus(EventStatus stored, DateTimeOffset? endsAt, DateTimeOffset now) =>
        stored == EventStatus.Live && endsAt is { } ends && ends <= now ? EventStatus.Past : stored;

    /// <remarks>
    /// Only for <see cref="WriteOperationKind.UpdateResource"/>, and only when the PATCH actually targeted
    /// <see cref="Event.Status"/> (<see cref="ITargetedFields.Attributes"/>) - an ordinary Save changes that
    /// never touches Status skips the lookup below entirely. JsonApiDotNetCore's
    /// <c>EntityFrameworkCoreRepository.UpdateAsync</c> copies targeted attributes onto the database-loaded
    /// entity before <see cref="OnWritingAsync"/> runs, so <paramref name="resource"/>'s
    /// <see cref="Event.Status"/> here is already the PATCH's *target* value - the pre-PATCH value has to be
    /// re-read via <see cref="Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/>,
    /// the same pattern <see cref="CheckForConflictsAsync"/> already uses for its own cross-row check. Compares
    /// the *effective* stored status (<see cref="EffectiveStatus"/>), not the raw one, so "a Past Event can't
    /// be cancelled retroactively" (ADR-0044) falls out for free even though Past is stored as Live. A target
    /// of <see cref="EventStatus.Past"/> is illegal unconditionally, checked before the same-status allowance
    /// below - without that ordering, naming <c>Past</c> explicitly on a row that's already effectively Past
    /// (an elapsed Live Event) would read as a same-value no-op and slip through as a 204, which the AC
    /// doesn't carve out an exception for. A same-status re-PATCH of anything else (<c>Cancelled</c> to
    /// <c>Cancelled</c> included) is a legal no-op, not a 422 - a defensive allowance for a retried request;
    /// the Web client never constructs one deliberately.
    /// </remarks>
    private async Task ValidateStatusTransitionAsync(
        Event resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
    {
        if (writeOperation != WriteOperationKind.UpdateResource)
        {
            return;
        }

        AttrAttribute statusAttribute = ResourceType.GetAttributeByPropertyName(nameof(Event.Status));
        if (!_targetedFields.Attributes.Contains(statusAttribute))
        {
            return;
        }

        var stored = await _dbContext.Events.AsNoTracking()
            .Where(e => e.Id == resource.Id)
            .Select(e => new { e.Status, e.EndsAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return;
        }

        EventStatus from = EffectiveStatus(stored.Status, stored.EndsAt, _timeProvider.GetUtcNow());
        EventStatus to = resource.Status;

        bool legal = to != EventStatus.Past
            && (from == to || (from, to) is (EventStatus.Draft, EventStatus.Live) or (EventStatus.Live, EventStatus.Cancelled));

        if (!legal)
        {
            throw new JsonApiException(new ErrorObject(HttpStatusCode.UnprocessableEntity)
            {
                Title = "Invalid status change.",
                Detail = $"An Event cannot go from {from} to {to}.",
                Source = new ErrorSource { Pointer = "/data/attributes/status" }
            });
        }
    }

    /// <remarks>
    /// Mirrors what <see cref="Event.Create"/> does for in-process callers, since JsonApiDotNetCore never
    /// routes a POST body through that factory - see this type's <see cref="OnWritingAsync"/> remarks.
    /// Unconditional, not "if blank": neither <see cref="Event.Slug"/> nor <see cref="Event.Passcode"/> carry
    /// <see cref="AttrCapabilities.AllowCreate"/> (see their remarks on <c>Event.cs</c>), so JsonApiDotNetCore
    /// never lets a POST body populate either in the first place - a client wanting a specific Slug sets it
    /// via a follow-up PATCH.
    /// </remarks>
    private static void FillServerGeneratedDefaults(Event resource)
    {
        resource.Slug = SlugDerivation.From(resource.Name);
        resource.Passcode = PasscodeGenerator.Generate();
    }

    /// <remarks>
    /// A pre-check, not a <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> catch: SQL Server and
    /// SQLite (ADR-0014's test-vs-production split) report a unique-index violation through different
    /// provider error codes, so distinguishing which column collided - needed to name it on the JSON:API
    /// error's <c>source.pointer</c> - can't portably come from catching and inspecting that exception.
    /// Compares case-insensitively to match SQL Server's default (case-insensitive) collation, since SQLite's
    /// own default is case-sensitive and would otherwise let a same-database test pass while production's
    /// real unique index still rejects it. A genuine concurrent double-submit to <see cref="Event.Slug"/>
    /// still falls through to that column's unique index and surfaces as a 500 - accepted, not handled here.
    ///
    /// <see cref="Event.Name"/> carries no database index at all (ADR-0053) - this is the *only* place its
    /// uniqueness rule is enforced, so a concurrent double-submit on Name never even reaches a 500; it's a
    /// narrow, accepted race producing two same-named Events instead. The rule only ever considers
    /// non-terminal Events - an effectively <see cref="EventStatus.Past"/> or <see cref="EventStatus.Cancelled"/>
    /// row's Name is free to reuse (CONTEXT.md's Event entry) - via the same <see cref="EffectiveStatus"/>
    /// helper <see cref="OnSerialize"/> and <see cref="ValidateStatusTransitionAsync"/> use, so all three agree
    /// on what "Past" means. Unlike Name, <see cref="Event.Slug"/> is a permanent domain invariant (it's the
    /// route key) and keeps its unconditional database-backed check below.
    ///
    /// Skips the Slug check when <see cref="Event.Slug"/> is <see langword="null"/> - never true for
    /// <see cref="WriteOperationKind.CreateResource"/> (<see cref="FillServerGeneratedDefaults"/> always runs
    /// first), and true for <see cref="WriteOperationKind.UpdateResource"/> only if a caller explicitly PATCHes
    /// it to <see langword="null"/>, which falls through to the column's own <c>NOT NULL</c> constraint and
    /// surfaces as a 500 - an accepted edge case, not one this pre-check needs to anticipate.
    /// </remarks>
    private async Task CheckForConflictsAsync(Event resource, CancellationToken cancellationToken)
    {
        string normalizedName = resource.Name.ToUpperInvariant();
        DateTimeOffset now = _timeProvider.GetUtcNow();

        var candidates = await _dbContext.Events.AsNoTracking()
            .Where(e => e.Id != resource.Id && e.Name.ToUpper() == normalizedName)
            .Select(e => new { e.Status, e.EndsAt })
            .ToListAsync(cancellationToken);

        bool nameTaken = candidates.Any(c => EffectiveStatus(c.Status, c.EndsAt, now) is EventStatus.Draft or EventStatus.Live);

        bool slugTaken = false;
        if (resource.Slug is not null)
        {
            string normalizedSlug = resource.Slug.ToUpperInvariant();
            slugTaken = await _dbContext.Events.AsNoTracking()
                .AnyAsync(e => e.Id != resource.Id && e.Slug!.ToUpper() == normalizedSlug, cancellationToken);
        }

        if (!nameTaken && !slugTaken)
        {
            return;
        }

        var errors = new List<ErrorObject>();
        if (nameTaken)
        {
            errors.Add(ConflictError("name", "Name", resource.Name));
        }

        if (slugTaken)
        {
            errors.Add(ConflictError("slug", "Slug", resource.Slug!));
        }

        throw new JsonApiException(errors);
    }

    private static ErrorObject ConflictError(string attributeName, string displayName, string value) => new(HttpStatusCode.Conflict)
    {
        Title = "Resource conflict.",
        Detail = $"{displayName} '{value}' is already in use by another Event.",
        Source = new ErrorSource { Pointer = $"/data/attributes/{attributeName}" }
    };

    /// <remarks>
    /// Enforces the same two rules as <c>CK_Events_Dates_Ordered</c> (<see cref="VirtualLeadersGuideDbContext"/>),
    /// but the CHECK constraint alone can't produce a JSON:API error naming which attribute is wrong, and
    /// can't see a partial PATCH's merged state the way <paramref name="resource"/> here already does -
    /// JsonApiDotNetCore's <c>EntityFrameworkCoreRepository.UpdateAsync</c> copies targeted attributes onto
    /// the database-loaded entity before calling <see cref="OnWritingAsync"/>, so a PATCH setting only
    /// <see cref="Event.EndsAt"/> still sees the persisted <see cref="Event.StartsAt"/> here, and a PATCH
    /// clearing <see cref="Event.StartsAt"/> while <see cref="Event.EndsAt"/> remains set is still caught.
    /// The 422 status (this repo's first) rather than the 409 <see cref="CheckForConflictsAsync"/> uses is
    /// deliberate - see ADR-0042: an invalid range isn't a collision with another Event.
    /// </remarks>
    private static void ValidateDateRange(Event resource)
    {
        if (resource.EndsAt is null)
        {
            return;
        }

        if (resource.StartsAt is null)
        {
            throw new JsonApiException(new ErrorObject(HttpStatusCode.UnprocessableEntity)
            {
                Title = "Invalid date range.",
                Detail = "Set a start before setting an end.",
                Source = new ErrorSource { Pointer = "/data/attributes/startsAt" }
            });
        }

        if (resource.EndsAt <= resource.StartsAt)
        {
            throw new JsonApiException(new ErrorObject(HttpStatusCode.UnprocessableEntity)
            {
                Title = "Invalid date range.",
                Detail = "End must be after the start.",
                Source = new ErrorSource { Pointer = "/data/attributes/endsAt" }
            });
        }
    }

    private FilterExpression BuildAssignedEventsFilter(EventAccessPolicy policy)
    {
        AttrAttribute idAttribute = ResourceType.GetAttributeByPropertyName(nameof(Event.Id));
        var idChain = new ResourceFieldChainExpression(idAttribute);

        IImmutableSet<LiteralConstantExpression> constants = policy.AssignedEventIds.Count == 0
            ? ImmutableHashSet.Create(new LiteralConstantExpression(NoEventsSentinel))
            : policy.AssignedEventIds
                .Select(eventId => new LiteralConstantExpression(eventId))
                .ToImmutableHashSet();

        return new AnyExpression(idChain, constants);
    }

    private EventAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "EventResourceDefinition requires an active HttpContext."));

    private IJsonApiRequest GetRequest() =>
        _httpContextAccessor.HttpContext?.RequestServices.GetRequiredService<IJsonApiRequest>()
            ?? throw new InvalidOperationException("EventResourceDefinition requires an active HttpContext.");

    private static JsonApiException ForbiddenException() =>
        new(new ErrorObject(HttpStatusCode.Forbidden)
        {
            Title = "You do not have permission to access this Event."
        });
}
