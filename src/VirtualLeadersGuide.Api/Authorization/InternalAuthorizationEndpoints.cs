using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Authorization;

// Grant CRUD only - person CRUD already lives in Api.Identity.InternalIdentityEndpoints, and there is no
// separate domain User to duplicate it for (ADR-0024). Backs Web's ApiRoleGrantClient. Gated by the same
// X-Internal-Key fallback policy as every other Api endpoint (ADR-0015) - no per-endpoint auth attributes
// needed. Deliberately outside JsonApi's /api namespace - UserRole isn't a JSON:API resource until P2-8
// (#17).
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

    private static async Task<IResult> GetGrantsAsync(
        string id, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        // grant.Role!.Name is inlined here (not routed through a helper method) so EF Core can translate
        // the whole projection into one SQL query with a join, rather than requiring an explicit
        // .Include(g => g.Role) - navigating into a reference navigation directly inside Select is exactly
        // the shape EF's query translator handles.
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
            // Filtered unique index on (UserId, RoleId) WHERE EventId IS NULL, or (UserId, RoleId, EventId)
            // WHERE EventId IS NOT NULL - see VirtualLeadersGuideDbContext.OnModelCreating and ADR-0017.
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
