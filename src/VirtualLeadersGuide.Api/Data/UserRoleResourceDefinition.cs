using System.Net;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Serialization.Objects;
using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Authorization;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Enforces Admin-only access on <c>/api/roleGrants</c> (P2-8, #17): only an Admin may read, create, or
/// delete a <see cref="UserRole"/> grant, and even an Admin may not create or delete an Admin-role grant
/// through this resource - see ADR-0033. Also refuses an Event-scoped Director grant, create or delete,
/// for a User who separately holds Admin - see ADR-0051.
/// </summary>
/// <remarks>
/// Authorization lives here rather than a hand-written controller or ASP.NET Core middleware, the same
/// reasoning as <see cref="EventResourceDefinition"/> (ADR-0031). Unlike that type, there is no
/// collection-vs-single-resource asymmetry: <see cref="OnApplyFilter"/> throws 403 for a non-Admin on both
/// shapes, because a non-Admin's visible set here is always empty, never partially narrowed the way a
/// Director's Event set is - see ADR-0033's generalization of ADR-0031's asymmetry rule.
/// </remarks>
public sealed class UserRoleResourceDefinition : JsonApiResourceDefinition<UserRole, Guid>
{
    private const string NotAdminTitle = "You do not have permission to manage role grants.";

    private const string AdminGrantTitle =
        "Admin role grants can't be created or removed through this resource - see ADR-0008's config allowlist.";

    private const string AdminHoldsEventGrantTitle =
        "An Admin already has access to every event and can't hold a separate Event grant - see ADR-0051.";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;

    /// <summary>Constructs the definition with the services it needs to authorize <see cref="UserRole"/> writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">
    /// Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> for
    /// <see cref="CurrentPolicy"/>.
    /// </param>
    /// <param name="dbContext">
    /// Backs <see cref="CheckForConflictsAsync"/>'s duplicate-grant pre-check, <see cref="GrantForDeleteAsync"/>'s
    /// re-read, and <see cref="HoldsAdminAsync"/>'s ADR-0051 check.
    /// </param>
    public UserRoleResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every non-Admin request is rejected outright, collection or single alike - see this type's remarks for
    /// why that departs from <see cref="EventResourceDefinition.OnApplyFilter"/>'s silent-filter behavior.
    /// </remarks>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        if (!CurrentPolicy().CanRead)
        {
            throw ForbiddenException(NotAdminTitle);
        }

        return existingFilter;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Rejects any write from a non-Admin, then applies two further, separate refusals (order doesn't matter
    /// between them - each is a full rejection on its own): an Admin-role grant even from an Admin (ADR-0033),
    /// and - ADR-0051 - an Event-scoped Director grant for a User who separately holds Admin, since Admin
    /// already covers every Event with no Grant step (ADR-0035) and a Grant alongside it is invalid state, not
    /// a second permission. For <see cref="WriteOperationKind.DeleteResource"/>, JsonApiDotNetCore hands this
    /// method a placeholder <paramref name="resource"/> with only <see cref="IIdentifiable.StringId"/> set (its
    /// own documented behavior for delete/relationship operations), so <see cref="GrantForDeleteAsync"/>
    /// re-reads the real row from the database rather than trusting the placeholder's default values, which
    /// would otherwise let both refusals through unchecked. A delete for an id that no longer exists resolves
    /// that lookup to all-<see langword="null"/> and falls through to
    /// <see cref="JsonApiResourceDefinition{TResource,TId}.OnWritingAsync"/>'s own not-found handling, rather
    /// than being misreported as a blocked delete.
    /// </remarks>
    public override async Task OnWritingAsync(
        UserRole resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
    {
        var policy = CurrentPolicy();
        if (!policy.IsAdmin)
        {
            throw ForbiddenException(NotAdminTitle);
        }

        (int? roleId, string? userId, Guid? eventId) = writeOperation == WriteOperationKind.DeleteResource
            ? await GrantForDeleteAsync(resource.Id, cancellationToken)
            : (resource.RoleId, resource.UserId, resource.EventId);

        if (roleId == RoleIds.Admin)
        {
            throw ForbiddenException(AdminGrantTitle);
        }

        if (roleId == RoleIds.Director && eventId is not null && await HoldsAdminAsync(userId, cancellationToken))
        {
            throw ForbiddenException(AdminHoldsEventGrantTitle);
        }

        if (writeOperation == WriteOperationKind.CreateResource)
        {
            await CheckForConflictsAsync(resource, cancellationToken);
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
    }

    /// <remarks>
    /// A pre-check, not a <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> catch - same
    /// rationale as <see cref="EventResourceDefinition.CheckForConflictsAsync"/>: SQL Server and SQLite
    /// (ADR-0014) report a unique-index violation through different provider error codes. Covers both of
    /// <c>UserRoles</c>' filtered unique indexes (<c>IX_UserRoles_PlatformWide</c>, <c>IX_UserRoles_EventScoped</c>
    /// - <see cref="VirtualLeadersGuideDbContext"/>) with a single query, since both express the same rule -
    /// same user, same role, same scope - and a request either matches the platform-wide index or the
    /// event-scoped one depending on whether <see cref="UserRole.EventId"/> is set.
    /// </remarks>
    private async Task CheckForConflictsAsync(UserRole resource, CancellationToken cancellationToken)
    {
        bool exists = await _dbContext.DomainUserRoles.AsNoTracking().AnyAsync(grant =>
            grant.Id != resource.Id && grant.UserId == resource.UserId && grant.RoleId == resource.RoleId
            && grant.EventId == resource.EventId, cancellationToken);

        if (!exists)
        {
            return;
        }

        throw new JsonApiException(new ErrorObject(HttpStatusCode.Conflict)
        {
            Title = "Resource conflict.",
            Detail = "This User already holds this Role in this scope.",
            Source = new ErrorSource { Pointer = "/data" }
        });
    }

    /// <remarks>
    /// Widened past a bare <c>RoleId</c> read (this type's earlier shape) to also carry <c>UserId</c>/
    /// <c>EventId</c> - ADR-0051's guard needs both, and re-reading the row twice for two different guards
    /// would cost a second round trip for no benefit.
    /// </remarks>
    private async Task<(int? RoleId, string? UserId, Guid? EventId)> GrantForDeleteAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var grant = await _dbContext.DomainUserRoles.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new { g.RoleId, g.UserId, g.EventId })
            .FirstOrDefaultAsync(cancellationToken);

        return grant is null ? (null, null, null) : (grant.RoleId, grant.UserId, grant.EventId);
    }

    /// <summary>Whether <paramref name="userId"/> separately holds the platform-wide Admin Role - ADR-0051's guard.</summary>
    private async Task<bool> HoldsAdminAsync(string? userId, CancellationToken cancellationToken) =>
        userId is not null && await _dbContext.DomainUserRoles.AsNoTracking()
            .AnyAsync(grant => grant.UserId == userId && grant.RoleId == RoleIds.Admin, cancellationToken);

    private RoleGrantAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "UserRoleResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException(string title) =>
        new(new ErrorObject(HttpStatusCode.Forbidden) { Title = title });
}
