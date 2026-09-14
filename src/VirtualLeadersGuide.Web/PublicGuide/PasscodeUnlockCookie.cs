using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace VirtualLeadersGuide.Web.PublicGuide;

/// <summary>
/// The signed, per-Event cookie ADR-0003 calls for - grants a visitor's browser read access to one Event's
/// Leaders Guide once they've entered its Passcode (CONTEXT.md's Unlock entry; P4-2, #72).
/// </summary>
/// <remarks>
/// A purpose-isolated <see cref="IDataProtector"/> over a plain cookie, not a second ASP.NET Core
/// authentication scheme - see the linked ADR for why: this gate authorizes nothing beyond one page's own
/// read, and a second <see cref="System.Security.Claims.ClaimsPrincipal"/> alongside Identity's own would be
/// more machinery than the job needs. One cookie per unlocked Event (<c>vlg_guide_{eventId:N}</c>), not one
/// cookie listing every Event a visitor has unlocked - bounded size, and each Event's Unlock expires
/// independently rather than all together.
/// <para>
/// The protected payload carries both the Event id and its <c>PasscodeVersion</c> at the moment of unlock,
/// not just the version - the cookie *name* already scopes it to one Event, but the payload's own id is what
/// <see cref="IsUnlocked"/> checks against the caller's expected id, so a cookie renamed client-side to
/// another Event's cookie name still fails to validate rather than silently unlocking the wrong Event.
/// </para>
/// </remarks>
public sealed class PasscodeUnlockCookie(IDataProtectionProvider dataProtectionProvider)
{
    private const string ProtectorPurpose = "VirtualLeadersGuide.Web.PublicGuide.Unlock";

    /// <remarks>
    /// The floor every Unlock gets, regardless of the Event's own dates - load-bearing, not decorative: an
    /// Event's <c>EndsAt</c> may already be in the past (a <c>Past</c> Event's guide still works, ADR-0044),
    /// and without this floor the expiry formula below could compute an instant already behind "now",
    /// producing a cookie dead on arrival. See ADR-0057 for the full reasoning.
    /// </remarks>
    private static readonly TimeSpan MinimumWindow = TimeSpan.FromDays(30);

    /// <summary>How far past an Event's own end (or start, absent an end) an Unlock is still honored.</summary>
    private static readonly TimeSpan PostEventBuffer = TimeSpan.FromDays(7);

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    /// <summary>Grants the current visitor's browser an Unlock for <paramref name="eventId"/>.</summary>
    /// <param name="httpContext">The current request's context - the cookie is written to its response.</param>
    /// <param name="eventId">The Event being unlocked.</param>
    /// <param name="passcodeVersion">
    /// The Event's <c>PasscodeVersion</c> at the moment of unlock (from <c>PasscodeCheckResult</c>) - what a
    /// later visit's <see cref="IsUnlocked"/> check compares against (ADR-0057).
    /// </param>
    /// <param name="endsAt">The Event's <c>EndsAt</c>, if set - stretches the expiry past <see cref="MinimumWindow"/> for an Event far in the future.</param>
    /// <param name="startsAt">The Event's <c>StartsAt</c>, used only when <paramref name="endsAt"/> is unset.</param>
    public void Unlock(
        HttpContext httpContext, Guid eventId, int passcodeVersion, DateTimeOffset? endsAt, DateTimeOffset? startsAt)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string payload = FormatPayload(eventId, passcodeVersion);

        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Expires = ComputeExpiry(now, endsAt, startsAt)
        };

        httpContext.Response.Cookies.Append(CookieName(eventId), _protector.Protect(payload), options);
    }

    /// <summary>
    /// Whether the current visitor's browser already carries a valid, still-current Unlock for
    /// <paramref name="eventId"/>.
    /// </summary>
    /// <param name="httpContext">The current request's context - the cookie is read from its request.</param>
    /// <param name="eventId">The Event to check.</param>
    /// <param name="currentPasscodeVersion">
    /// The Event's <c>PasscodeVersion</c> right now (from a fresh lookup) - a cookie stamped with an older
    /// version reads as locked, identically to no cookie at all (ADR-0057: the Passcode was rotated since
    /// this browser unlocked it).
    /// </param>
    /// <returns>
    /// <see langword="false"/> for a missing, tampered, or version-stale cookie - every failure mode
    /// collapses to "not unlocked" rather than throwing, since none of them are actionable by the caller.
    /// </returns>
    public bool IsUnlocked(HttpContext httpContext, Guid eventId, int currentPasscodeVersion)
    {
        if (!httpContext.Request.Cookies.TryGetValue(CookieName(eventId), out string? protectedValue))
        {
            return false;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return false;
        }

        return TryParsePayload(payload, out Guid payloadEventId, out int payloadVersion)
            && payloadEventId == eventId
            && payloadVersion == currentPasscodeVersion;
    }

    private static string CookieName(Guid eventId) => $"vlg_guide_{eventId:N}";

    private static string FormatPayload(Guid eventId, int passcodeVersion) =>
        $"{eventId:N}|{passcodeVersion.ToString(CultureInfo.InvariantCulture)}";

    private static bool TryParsePayload(string payload, out Guid eventId, out int passcodeVersion)
    {
        eventId = Guid.Empty;
        passcodeVersion = 0;

        string[] parts = payload.Split('|');
        return parts.Length == 2
            && Guid.TryParseExact(parts[0], "N", out eventId)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out passcodeVersion);
    }

    private static DateTimeOffset ComputeExpiry(DateTimeOffset now, DateTimeOffset? endsAt, DateTimeOffset? startsAt)
    {
        DateTimeOffset floor = now + MinimumWindow;

        if ((endsAt ?? startsAt) is not { } anchor)
        {
            return floor;
        }

        DateTimeOffset stretched = anchor + PostEventBuffer;
        return stretched > floor ? stretched : floor;
    }
}
