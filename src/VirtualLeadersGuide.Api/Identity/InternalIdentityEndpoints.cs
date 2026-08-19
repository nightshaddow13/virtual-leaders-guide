using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Data;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Identity;

/// <summary>Plain CRUD-by-user endpoints against Api's own <see cref="IdentityDbContext{TUser}"/>.</summary>
/// <remarks>
/// Backs Web's <c>ApiUserStore</c> (ADR-0022). Deliberately not routed through
/// Microsoft.AspNetCore.Identity's <c>UserManager</c>/EF <c>UserStore</c> here - those concerns (password
/// hashing, token providers, ...) live entirely in Web, the only place
/// <c>SignInManager</c>/<c>UserManager</c> run. Gated by the same <c>X-Internal-Key</c> fallback policy as
/// every other Api endpoint (ADR-0015), and deliberately outside JsonApi's <c>/api</c> namespace - these
/// aren't a JSON:API resource.
/// </remarks>
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

    /// <remarks>
    /// <see cref="IdentityUserDto.Id"/>/<see cref="IdentityUserDto.ConcurrencyStamp"/> already come
    /// populated from the caller: Web constructs <c>new ApplicationUser()</c>, and <c>IdentityUser</c>'s
    /// own parameterless constructor assigns both before <c>UserManager.CreateAsync</c> ever reaches
    /// <c>ApiUserStore</c> - so this is a plain insert, not a generator. A <see cref="DbUpdateException"/>
    /// here means the unique index on <c>NormalizedUserName</c> (<c>IdentityDbContext</c>'s default
    /// <c>"UserNameIndex"</c>) - the practical email-uniqueness enforcement, since <c>UserName</c> is set
    /// equal to <c>Email</c> everywhere this app creates a user (no separate username concept - see
    /// CONTEXT.md's User/Credential entries).
    /// </remarks>
    private static async Task<IResult> CreateAsync(
        IdentityUserDto dto, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        db.Users.Add(FromDto(dto));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict();
        }

        return Results.Created(InternalIdentityRoutes.ForUserById(dto.Id), dto);
    }

    /// <remarks>
    /// Mirrors the stock EF Core Identity <c>UserStore.UpdateAsync</c> pattern exactly (Attach, then bump
    /// the stamp, then Update): <c>Attach</c> captures <c>dto.ConcurrencyStamp</c> - the value the caller
    /// last read - as the row's ORIGINAL value for the WHERE clause; reassigning
    /// <see cref="ApplicationUser.ConcurrencyStamp"/> before <c>Update()</c> changes only the value being
    /// WRITTEN. If the row's real stamp has since moved on, 0 rows match and EF throws
    /// <see cref="DbUpdateConcurrencyException"/> - see ADR-0022's Consequences for why this concurrency
    /// check is kept at all. A <see cref="DbUpdateConcurrencyException"/> here covers a stale
    /// ConcurrencyStamp, the row having been deleted since the caller read it, and (in practice unreachable
    /// here, since Update always follows a successful FindBy*) the row never having existed at all - all
    /// three are usefully signalled to Web as <c>ConcurrencyFailure</c>.
    /// </remarks>
    private static async Task<IResult> UpdateAsync(
        string id, IdentityUserDto dto, VirtualLeadersGuideDbContext db, CancellationToken cancellationToken)
    {
        if (id != dto.Id)
        {
            return Results.BadRequest();
        }

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
        DisplayName = user.DisplayName,
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
        DisplayName = dto.DisplayName,
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
