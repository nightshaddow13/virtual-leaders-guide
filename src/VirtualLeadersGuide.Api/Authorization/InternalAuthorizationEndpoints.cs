using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Authorization;

/// <summary>Grant CRUD, backing Web's <c>ApiRoleGrantClient</c> over the internal channel.</summary>
/// <remarks>
/// Person CRUD lives separately in <c>InternalIdentityEndpoints</c> (ADR-0022) - see ADR-0024's
/// Consequences for why there's no separate domain-User CRUD surface here. Gated by the same
/// <c>X-Internal-Key</c> fallback policy as every other Api endpoint (ADR-0015), and deliberately outside
/// JsonApi's <c>/api</c> namespace - <see cref="UserRole"/> isn't a JSON:API resource yet (ADR-0017).
/// </remarks>
public static class InternalAuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapInternalAuthorizationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup(InternalAuthorizationRoutes.GroupPrefix);

        group.MapGet(InternalAuthorizationRoutes.UserGrants, GetGrantsAsync);
        group.MapPost(InternalAuthorizationRoutes.UserGrants, CreateGrantAsync);
        group.MapDelete(InternalAuthorizationRoutes.UserGrantById, DeleteGrantAsync);

        return app;
    }

    /// <remarks>
    /// <c>grant.Role!.Name</c> is inlined here (not routed through a helper method) so EF Core can translate
    /// the whole projection into one SQL query with a join, rather than requiring an explicit
    /// <c>.Include(g =&gt; g.Role)</c> - navigating into a reference navigation directly inside
    /// <c>Select</c> is exactly the shape EF's query translator handles.
    /// </remarks>
    private static async Task<IResult> GetGrantsAsync(
        string id, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        List<RoleGrantDto> grants = await db.DomainUserRoles.AsNoTracking()
            .Where(grant => grant.UserId == id)
            .Select(grant => new RoleGrantDto
            {
                Id = grant.Id,
                RoleId = grant.RoleId,
                RoleName = grant.Role!.Name,
                EventId = grant.EventId
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(grants);
    }

    /// <remarks>
    /// The filtered unique indexes on <c>UserRoles</c> (<see cref="VirtualLeadersGuideDbContext"/>,
    /// ADR-0017) are what a <see cref="DbUpdateException"/> here means.
    /// </remarks>
    private static async Task<IResult> CreateGrantAsync(
        string id, CreateRoleGrantRequest request, VirtualLeadersGuideDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        Role? role = await db.DomainRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        if (role is null)
        {
            return Results.NotFound();
        }

        var grant = new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = id,
            RoleId = request.RoleId,
            EventId = request.EventId
        };

        db.DomainUserRoles.Add(grant);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict();
        }

        var dto = new RoleGrantDto { Id = grant.Id, RoleId = role.Id, RoleName = role.Name, EventId = grant.EventId };
        return Results.Created(InternalAuthorizationRoutes.ForUserGrantById(id, grant.Id), dto);
    }

    private static async Task<IResult> DeleteGrantAsync(
        string id, Guid grantId, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        int affected = await db.DomainUserRoles
            .Where(grant => grant.UserId == id && grant.Id == grantId)
            .ExecuteDeleteAsync(cancellationToken);

        return affected == 0 ? Results.NotFound() : Results.NoContent();
    }
}
