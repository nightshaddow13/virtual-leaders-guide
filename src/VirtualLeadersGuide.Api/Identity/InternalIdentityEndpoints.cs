using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Identity;

// Backs Web's ApiUserStore (an IUserStore<ApplicationUser> implemented over HTTP - see ADR-0022) with plain
// CRUD-by-user endpoints against Api's own IdentityDbContext. Deliberately not routed through
// Microsoft.AspNetCore.Identity's UserManager/EF UserStore on this side - Api has no reason to run the full
// Identity pipeline (password hashing, token providers, ...) just to read/write rows; those concerns live
// entirely in Web, the only place SignInManager/UserManager run. Gated by the same X-Internal-Key fallback
// policy as every other Api endpoint (ADR-0015) - no per-endpoint auth attributes needed. Deliberately
// outside JsonApi's /api namespace - these aren't a JSON:API resource.
public static class InternalIdentityEndpoints
{
    public static IEndpointRouteBuilder MapInternalIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup(InternalIdentityRoutes.GroupPrefix);

        group.MapGet(InternalIdentityRoutes.UserById, GetByIdAsync);
        group.MapGet(InternalIdentityRoutes.UserByNormalizedUserName, GetByNormalizedUserNameAsync);
        group.MapGet(InternalIdentityRoutes.UserByNormalizedEmail, GetByNormalizedEmailAsync);
        group.MapPost(InternalIdentityRoutes.Users, CreateAsync);
        group.MapPut(InternalIdentityRoutes.UserById, UpdateAsync);
        group.MapDelete(InternalIdentityRoutes.UserById, DeleteAsync);

        return app;
    }

    private static async Task<IResult> GetByIdAsync(
        string id, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(ToDto(user));
    }

    private static async Task<IResult> GetByNormalizedUserNameAsync(
        string normalizedUserName, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(ToDto(user));
    }

    private static async Task<IResult> GetByNormalizedEmailAsync(
        string normalizedEmail, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(ToDto(user));
    }

    private static async Task<IResult> CreateAsync(
        IdentityUserDto dto, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        // Id and ConcurrencyStamp already come populated from the caller: Web constructs `new
        // ApplicationUser()`, and IdentityUser's own parameterless constructor assigns both before
        // UserManager.CreateAsync ever reaches ApiUserStore - so this is a plain insert, not a generator.
        db.Users.Add(FromDto(dto));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique index on NormalizedUserName (IdentityDbContext's default "UserNameIndex") - the
            // practical email-uniqueness enforcement, since UserName is set equal to Email everywhere this
            // app creates a user (no separate username concept - see CONTEXT.md's User/Credential entries).
            return Results.Conflict();
        }

        return Results.Created(InternalIdentityRoutes.ForUserById(dto.Id), dto);
    }

    private static async Task<IResult> UpdateAsync(
        string id, IdentityUserDto dto, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        if (id != dto.Id)
        {
            return Results.BadRequest();
        }

        // Mirrors the stock EF Core Identity UserStore.UpdateAsync pattern exactly (Attach, then bump the
        // stamp, then Update): Attach captures dto.ConcurrencyStamp - the value the caller last read - as
        // the row's ORIGINAL value for the WHERE clause; reassigning ConcurrencyStamp before Update()
        // changes only the value being WRITTEN. If the row's real stamp has since moved on, 0 rows match
        // and EF throws DbUpdateConcurrencyException below - kept per ADR-0022 even though real conflict
        // risk at this app's scale is low, to match the framework's own contract rather than special-case
        // it away.
        ApplicationUser entity = FromDto(dto);
        db.Attach(entity);
        entity.ConcurrencyStamp = Guid.NewGuid().ToString();
        db.Update(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Covers a stale ConcurrencyStamp, the row having been deleted since the caller read it, and
            // (in practice unreachable here, since Update always follows a successful FindBy*) the row
            // never having existed at all - all three are usefully signalled to Web as ConcurrencyFailure.
            return Results.Conflict();
        }

        return Results.Ok(ToDto(entity));
    }

    private static async Task<IResult> DeleteAsync(
        string id, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        int affected = await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken);
        return affected == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static IdentityUserDto ToDto(ApplicationUser user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        NormalizedUserName = user.NormalizedUserName,
        Email = user.Email,
        NormalizedEmail = user.NormalizedEmail,
        EmailConfirmed = user.EmailConfirmed,
        PasswordHash = user.PasswordHash,
        SecurityStamp = user.SecurityStamp,
        ConcurrencyStamp = user.ConcurrencyStamp
            ?? throw new InvalidOperationException("ConcurrencyStamp must always be set."),
        PhoneNumber = user.PhoneNumber,
        PhoneNumberConfirmed = user.PhoneNumberConfirmed,
        TwoFactorEnabled = user.TwoFactorEnabled,
        LockoutEnd = user.LockoutEnd,
        LockoutEnabled = user.LockoutEnabled,
        AccessFailedCount = user.AccessFailedCount
    };

    private static ApplicationUser FromDto(IdentityUserDto dto) => new()
    {
        Id = dto.Id,
        UserName = dto.UserName,
        NormalizedUserName = dto.NormalizedUserName,
        Email = dto.Email,
        NormalizedEmail = dto.NormalizedEmail,
        EmailConfirmed = dto.EmailConfirmed,
        PasswordHash = dto.PasswordHash,
        SecurityStamp = dto.SecurityStamp,
        ConcurrencyStamp = dto.ConcurrencyStamp,
        PhoneNumber = dto.PhoneNumber,
        PhoneNumberConfirmed = dto.PhoneNumberConfirmed,
        TwoFactorEnabled = dto.TwoFactorEnabled,
        LockoutEnd = dto.LockoutEnd,
        LockoutEnabled = dto.LockoutEnabled,
        AccessFailedCount = dto.AccessFailedCount
    };
}
