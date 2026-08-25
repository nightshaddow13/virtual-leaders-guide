using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.E2E.Tests;

/// <summary>
/// Seeds and mutates local-Identity accounts directly against <c>Api</c>'s internal identity endpoints
/// (<see cref="InternalIdentityRoutes"/>), for tests that need a real, sign-in-able account before driving
/// the actual rendered Login form.
/// </summary>
/// <remarks>
/// Mirrors <c>Tools.SeedUser</c>'s <see cref="IdentityUserDto"/> + <see cref="PasswordHasher{TUser}"/> recipe
/// (P2-2, #11) so a seeded account hashes its password exactly the way a real sign-up would. Every method
/// throws on a non-success response rather than returning a soft outcome: every test seeds a fresh,
/// guid-suffixed email (see the naming convention on each test file), so a failure here - a 409 Conflict, a
/// timeout, anything - can only mean a test-infrastructure bug, never a legitimate race the way it can for
/// <c>ApiRoleGrantClient.CreateGrantAsync</c>'s production callers.
/// </remarks>
public sealed class IdentityApiClient(HttpClient httpClient)
{
    /// <summary>
    /// Creates a local-Identity account for <paramref name="email"/>/<paramref name="password"/>, with
    /// <see cref="IdentityUserDto.EmailConfirmed"/> and <see cref="IdentityUserDto.LockoutEnabled"/> both
    /// <see langword="true"/> (mirroring <c>Tools.SeedUser</c>), so the account can sign in immediately.
    /// </summary>
    /// <param name="email">The account's email - also used as its username (no separate username concept).</param>
    /// <param name="password">The account's plaintext password, hashed here before it ever reaches <c>Api</c>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created account, as <c>Api</c> stored it.</returns>
    /// <exception cref="InvalidOperationException">The create request did not succeed.</exception>
    public async Task<IdentityUserDto> CreateUserAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        string normalizedEmail = email.ToUpperInvariant();
        var user = new IdentityUserDto
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = normalizedEmail,
            Email = email,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            PasswordHash = new PasswordHasher<object>().HashPassword(new object(), password),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };

        using HttpResponseMessage response =
            await httpClient.PostAsJsonAsync(InternalIdentityRoutes.ForUsers(), user, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync("create", email, response, cancellationToken);
        }

        return user;
    }

    /// <summary>
    /// Sets <paramref name="user"/>'s <see cref="IdentityUserDto.LockoutEnd"/>, locking the account out until
    /// that time.
    /// </summary>
    /// <param name="user">
    /// The account to lock out, as previously returned by <see cref="CreateUserAsync"/> - its
    /// <see cref="IdentityUserDto.ConcurrencyStamp"/> must still be current, which it is as long as nothing
    /// else has written to the row since.
    /// </param>
    /// <param name="lockoutEnd">The time the lockout expires.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated account, as <c>Api</c> stored it.</returns>
    /// <exception cref="InvalidOperationException">
    /// The update request did not succeed - including a 409 Conflict from a stale
    /// <see cref="IdentityUserDto.ConcurrencyStamp"/>.
    /// </exception>
    public async Task<IdentityUserDto> LockOutAsync(
        IdentityUserDto user, DateTimeOffset lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;

        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            InternalIdentityRoutes.ForUserById(user.Id), user, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync("lock out", user.Email ?? user.Id, response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<IdentityUserDto>(cancellationToken))!;
    }

    /// <summary>
    /// Whether a User exists for <paramref name="email"/>, asked of <c>Api</c> directly rather than inferred
    /// from the UI - for tests proving a self-deleted account is actually gone.
    /// </summary>
    /// <param name="email">The email to look up - normalized the same way <see cref="CreateUserAsync"/> does.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if a User row exists for <paramref name="email"/>, otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Unlike <see cref="CreateUserAsync"/>/<see cref="LockOutAsync"/>, a 404 here is a legitimate answer, not
    /// a failure - this class's header remarks' "every method throws on non-success" rule only ever covered
    /// mutating calls whose whole precondition is a fresh, not-yet-existing email.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The lookup returned neither 200 nor 404.</exception>
    public async Task<bool> ExistsAsync(string email, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            InternalIdentityRoutes.ForUserByNormalizedEmail(email.ToUpperInvariant()), cancellationToken);

        if (response.StatusCode is HttpStatusCode.OK)
        {
            return true;
        }

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        await ThrowForFailureAsync("look up", email, response, cancellationToken);
        return false;
    }

    /// <summary>Grants <paramref name="userId"/> the Director Role, scoped to <paramref name="eventId"/>.</summary>
    /// <param name="userId">The <c>ApplicationUser.Id</c> to grant - <see cref="IdentityUserDto.Id"/> from <see cref="CreateUserAsync"/>.</param>
    /// <param name="eventId">The Event to scope the grant to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>
    /// Hits the same <see cref="InternalAuthorizationRoutes.ForUserGrants"/> endpoint
    /// <c>ApiRoleGrantClient.CreateGrantAsync</c> (Web) calls in production, over this class's plain
    /// <c>X-Internal-Key</c>-only client - the endpoint sits outside <c>/api</c>'s internal-JWT policy (see
    /// <see cref="InternalAuthorizationRoutes"/>'s own remarks), so no bearer token is needed here.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The grant request did not succeed.</exception>
    public async Task GrantDirectorAsync(string userId, Guid eventId, CancellationToken cancellationToken)
    {
        var request = new CreateRoleGrantRequest { RoleId = RoleIds.Director, EventId = eventId };

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            InternalAuthorizationRoutes.ForUserGrants(userId), request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync("grant Director to", userId, response, cancellationToken);
        }
    }

    private static async Task ThrowForFailureAsync(
        string action, string subject, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"IdentityApiClient failed to {action} '{subject}': {(int)response.StatusCode} " +
            $"{response.StatusCode}. {body}");
    }
}
