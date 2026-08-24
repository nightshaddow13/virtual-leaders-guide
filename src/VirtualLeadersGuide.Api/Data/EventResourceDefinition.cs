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
/// accidental (ADR-0031).
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

    /// <summary>Constructs the definition with the services it needs to authorize and default Event writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">
    /// Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> (for
    /// <see cref="EventAccessPolicy"/>) and <see cref="IJsonApiRequest"/> - see <see cref="CurrentPolicy"/> and
    /// <see cref="GetRequest"/>.
    /// </param>
    /// <param name="dbContext">Backs <see cref="CheckForConflictsAsync"/>'s Name/Slug uniqueness pre-check.</param>
    public EventResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An Admin's filter passes through unchanged. Otherwise, the request is narrowed to
    /// <c>Id IN (assigned event ids)</c>, ANDed with whatever filter the caller already supplied - a
    /// Director's <c>GET /api/events?filter=...</c> filters within their own Events, never sees others. A
    /// single-resource request (<see cref="IJsonApiRequest.PrimaryId"/> set) is checked directly instead and
    /// throws 403 when denied - see this type's remarks for why that differs from the collection case.
    /// </remarks>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        var policy = CurrentPolicy();
        if (policy.IsAdmin)
        {
            return existingFilter;
        }

        IJsonApiRequest request = GetRequest();
        if (request.PrimaryId is not null)
        {
            if (!policy.CanRead(Guid.Parse(request.PrimaryId)))
            {
                throw ForbiddenException();
            }

            return existingFilter;
        }

        FilterExpression scopeFilter = BuildAssignedEventsFilter(policy);
        return existingFilter is null ? scopeFilter : new LogicalExpression(LogicalOperator.And, existingFilter, scopeFilter);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Authorizes the write per <see cref="EventAccessPolicy"/> first, then - <see cref="WriteOperationKind.CreateResource"/>
    /// only - generates <see cref="Event.Slug"/>/<see cref="Event.Passcode"/> server-side, the same way
    /// <see cref="Event.Create"/> does for in-process callers (JsonApiDotNetCore constructs <see cref="Event"/>
    /// through its own resource factory, never through <see cref="Event.Create"/>) - see
    /// <see cref="FillServerGeneratedDefaults"/> for why this is unconditional rather than "if blank". Finally
    /// pre-checks the <see cref="Event.Name"/>/<see cref="Event.Slug"/> unique indexes and throws a 409 naming
    /// whichever collided - see <see cref="CheckForConflictsAsync"/> for why this is a pre-check rather than
    /// catching the eventual <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>.
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
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
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
    /// real unique index still rejects it. A genuine concurrent double-submit still falls through to the
    /// unique index itself and surfaces as a 500 - accepted, not handled here.
    ///
    /// <see cref="Event.Name"/>'s check enforces today's provisional plain unique index (P2-6, #15) - see
    /// that property's remarks for why it's expected to loosen once Event archiving exists, unlike
    /// <see cref="Event.Slug"/>'s, which is a permanent domain invariant (it's the route key).
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

        bool nameTaken = await _dbContext.Events.AsNoTracking()
            .AnyAsync(e => e.Id != resource.Id && e.Name.ToUpper() == normalizedName, cancellationToken);

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
