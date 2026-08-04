namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// The single formatter/parser for a <see cref="System.Security.Claims.ClaimTypes.Role"/> claim value derived
/// from a <see cref="RoleGrantDto"/>, per ADR-0007/ADR-0017. Shared here so <c>Web</c>'s cookie-claims factory,
/// <c>Web</c>'s internal-JWT minting, and <c>Api</c>'s claim consumption can never disagree about what a role
/// claim's string value means.
/// </summary>
/// <remarks>
/// A platform-wide grant (<see cref="RoleGrantDto.EventId"/> is <see langword="null"/>, e.g. <c>Admin</c>)
/// formats as the bare role name. An Event-scoped grant (e.g. <c>Director</c>) formats as
/// <c>"{RoleName}:{EventId}"</c>, per ADR-0007's own example (<c>Director:eventId</c>) - this is the only
/// place scoping information about a role grant survives onto a claim, so both sides must go through this
/// type rather than reformatting a <see cref="RoleGrantDto"/> by hand.
/// </remarks>
public static class RoleClaimValue
{
    private const char Separator = ':';

    /// <summary>
    /// Formats a role grant as the string value of a <see cref="System.Security.Claims.ClaimTypes.Role"/> claim.
    /// </summary>
    /// <param name="grant">The grant to format.</param>
    /// <returns>
    /// <paramref name="grant"/>'s <see cref="RoleGrantDto.RoleName"/> alone when
    /// <see cref="RoleGrantDto.EventId"/> is <see langword="null"/> (a platform-wide grant); otherwise
    /// <c>"{RoleName}:{EventId}"</c> (an Event-scoped grant).
    /// </returns>
    public static string Format(RoleGrantDto grant) =>
        grant.EventId is null ? grant.RoleName : $"{grant.RoleName}{Separator}{grant.EventId}";

    /// <summary>
    /// Attempts to parse a claim value previously produced by <see cref="Format"/> back into its role name and
    /// optional Event scope.
    /// </summary>
    /// <param name="value">The claim value to parse.</param>
    /// <param name="roleName">
    /// When this method returns <see langword="true"/>, the role name portion of <paramref name="value"/>
    /// (e.g. <c>"Admin"</c> or <c>"Director"</c>); otherwise <see cref="string.Empty"/>.
    /// </param>
    /// <param name="eventId">
    /// When this method returns <see langword="true"/>, the Event id the grant is scoped to, or
    /// <see langword="null"/> for a platform-wide grant.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="value"/> is a bare role name, or a role name followed by
    /// <c>':'</c> and a valid <see cref="Guid"/>; <see langword="false"/> otherwise (including a role name
    /// followed by a malformed Event id, which this method treats as unparseable rather than silently
    /// dropping the scope).
    /// </returns>
    public static bool TryParse(string value, out string roleName, out Guid? eventId)
    {
        int separatorIndex = value.IndexOf(Separator);
        if (separatorIndex < 0)
        {
            roleName = value;
            eventId = null;
            return true;
        }

        if (Guid.TryParse(value.AsSpan(separatorIndex + 1), out Guid parsedEventId))
        {
            roleName = value[..separatorIndex];
            eventId = parsedEventId;
            return true;
        }

        roleName = string.Empty;
        eventId = null;
        return false;
    }
}
