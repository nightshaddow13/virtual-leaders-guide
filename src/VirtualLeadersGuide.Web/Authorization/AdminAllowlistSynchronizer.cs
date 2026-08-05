using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Authorization;

/// <summary>
/// Promotes or demotes a signing-in <see cref="ApplicationUser"/>'s platform-wide Admin grant to match the
/// config-driven Admin allowlist (<see cref="AdminAllowlistOptions"/>), per ADR-0008.
/// </summary>
/// <remarks>
/// Called from <see cref="ApplicationUserClaimsPrincipalFactory.GenerateClaimsAsync"/>, which fires both on an
/// actual sign-in and on <c>SignInManager.RefreshSignInAsync</c> (password change, profile update) - the sync
/// runs on both rather than threading a sign-in-only flag through, shortening the staleness window beyond
/// ADR-0008's "every login" minimum, consistent with ADR-0006's "checked on every request" philosophy. The
/// only "cost" is a write exactly when one is actually due; there's no cost when nothing changed.
/// </remarks>
public sealed class AdminAllowlistSynchronizer(
    ApiRoleGrantClient roleGrantClient,
    IOptions<AdminAllowlistOptions> options,
    ILookupNormalizer normalizer,
    ILogger<AdminAllowlistSynchronizer> logger)
{
    private static readonly char[] Separators = [';', ','];

    /// <summary>
    /// Reads <paramref name="user"/>'s current grants, promotes or demotes their platform-wide Admin grant to
    /// match the Admin allowlist, and returns the resulting grant list.
    /// </summary>
    /// <param name="user">The user signing in.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <paramref name="user"/>'s grants after syncing, or <see langword="null"/> if their row no longer exists
    /// on <c>Api</c> - no write is attempted in that case, matching
    /// <see cref="ApiRoleGrantClient.GetGrantsAsync"/>'s own null-on-not-found contract.
    /// </returns>
    /// <exception cref="AuthorizationDataUnavailableException">
    /// The authorization store is unreachable or returned an unexpected response, for either the read or the
    /// promote/demote write. Left to propagate rather than swallowed - config is authoritative (ADR-0008), so
    /// a sign-in that can't confirm it matches config should fail rather than stamp a cookie that might
    /// already be stale.
    /// </exception>
    public async Task<IReadOnlyList<RoleGrantDto>?> SyncAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        IReadOnlyList<RoleGrantDto>? grants = await roleGrantClient.GetGrantsAsync(user.Id, cancellationToken);
        if (grants is null)
        {
            return null;
        }

        RoleGrantDto? adminGrant = grants.FirstOrDefault(g => g.RoleId == RoleIds.Admin && g.EventId is null);
        bool shouldBeAdmin = IsAllowlisted(user);

        if (shouldBeAdmin && adminGrant is null)
        {
            return await PromoteAsync(user, grants, cancellationToken);
        }

        if (!shouldBeAdmin && adminGrant is not null)
        {
            return await DemoteAsync(user, grants, adminGrant, cancellationToken);
        }

        return grants;
    }

    private bool IsAllowlisted(ApplicationUser user)
    {
        string? normalizedUserEmail = user.NormalizedEmail
            ?? (user.Email is null ? null : normalizer.NormalizeEmail(user.Email));
        if (normalizedUserEmail is null)
        {
            return false;
        }

        return options.Value.Emails
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(email => normalizer.NormalizeEmail(email) == normalizedUserEmail);
    }

    private async Task<IReadOnlyList<RoleGrantDto>> PromoteAsync(
        ApplicationUser user, IReadOnlyList<RoleGrantDto> grants, CancellationToken cancellationToken)
    {
        (GrantCreationOutcome outcome, RoleGrantDto? grant) =
            await roleGrantClient.CreateGrantAsync(user.Id, RoleIds.Admin, eventId: null, cancellationToken);

        switch (outcome)
        {
            case GrantCreationOutcome.Created:
                logger.LogInformation("Promoted user {UserId} to Admin via the Admin allowlist.", user.Id);
                return [.. grants, grant!];

            case GrantCreationOutcome.AlreadyGranted:
                // A concurrent sign-in already created it - not an error, see
                // ApiRoleGrantClient.CreateGrantAsync's own comment on why this outcome isn't an exception.
                return grants;

            default: // UserOrRoleNotFound - the row vanished between the read above and this write.
                logger.LogWarning(
                    "Could not promote user {UserId} to Admin - Api no longer has a row for them.", user.Id);
                return grants;
        }
    }

    private async Task<IReadOnlyList<RoleGrantDto>> DemoteAsync(
        ApplicationUser user, IReadOnlyList<RoleGrantDto> grants, RoleGrantDto adminGrant, CancellationToken cancellationToken)
    {
        bool deleted = await roleGrantClient.DeleteGrantAsync(user.Id, adminGrant.Id, cancellationToken);
        if (deleted)
        {
            logger.LogInformation("Demoted user {UserId} from Admin via the Admin allowlist.", user.Id);
        }

        return [.. grants.Where(g => g.Id != adminGrant.Id)];
    }
}
