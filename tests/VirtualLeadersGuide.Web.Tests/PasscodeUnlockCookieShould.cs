using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using VirtualLeadersGuide.Web.PublicGuide;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Round-trips through real <c>Set-Cookie</c>/<c>Cookie</c> headers - <see cref="PasscodeUnlockCookie.Unlock"/>
/// writes to one <see cref="DefaultHttpContext"/>'s response, <see cref="ReadingContextAfter"/> replays
/// whatever it wrote as the next request's <c>Cookie</c> header, matching what a real browser does between
/// visits. Uses an ephemeral (in-memory, unpersisted) <see cref="IDataProtectionProvider"/> - nothing here
/// needs to survive past the test.
/// </remarks>
/// <remarks>
/// ADR-0057 coverage: a stale-version cookie reads as locked - the whole point of binding an Unlock to the
/// Passcode it was made with, so rotating the Passcode revokes it. The expiry floor is load-bearing, not
/// decorative - without it, unlocking a Past Event (still fully working, ADR-0044) after <c>EndsAt</c> has
/// already elapsed would compute an expiry already behind "now"; the far-future-<c>EndsAt</c> case is the
/// other side of that same formula, an early unlock that shouldn't relock before the Event even happens.
/// </remarks>
public class PasscodeUnlockCookieShould
{
    [Fact]
    public void ReportUnlocked_WhenTheCookieMatchesTheCurrentVersion_ForIsUnlocked()
    {
        PasscodeUnlockCookie cookie = CreateCookie();
        var eventId = Guid.NewGuid();
        DefaultHttpContext writeContext = new();

        cookie.Unlock(writeContext, eventId, passcodeVersion: 1, endsAt: null, startsAt: null);
        HttpContext readContext = ReadingContextAfter(writeContext);

        Assert.True(cookie.IsUnlocked(readContext, eventId, currentPasscodeVersion: 1));
    }

    [Fact]
    public void ReportLocked_WhenNoCookieWasEverSet_ForIsUnlocked()
    {
        PasscodeUnlockCookie cookie = CreateCookie();

        Assert.False(cookie.IsUnlocked(new DefaultHttpContext(), Guid.NewGuid(), currentPasscodeVersion: 1));
    }

    [Fact]
    public void ReportLocked_WhenTheCookieBelongsToADifferentEvent_ForIsUnlocked()
    {
        PasscodeUnlockCookie cookie = CreateCookie();
        DefaultHttpContext writeContext = new();
        cookie.Unlock(writeContext, Guid.NewGuid(), passcodeVersion: 1, endsAt: null, startsAt: null);
        HttpContext readContext = ReadingContextAfter(writeContext);

        Assert.False(cookie.IsUnlocked(readContext, Guid.NewGuid(), currentPasscodeVersion: 1));
    }

    [Fact]
    public void ReportLocked_WhenTheCookieValueIsTampered_ForIsUnlocked()
    {
        PasscodeUnlockCookie cookie = CreateCookie();
        var eventId = Guid.NewGuid();
        DefaultHttpContext writeContext = new();
        cookie.Unlock(writeContext, eventId, passcodeVersion: 1, endsAt: null, startsAt: null);
        HttpContext readContext = ReadingContextAfter(writeContext, tamper: true);

        Assert.False(cookie.IsUnlocked(readContext, eventId, currentPasscodeVersion: 1));
    }

    [Fact]
    public void ReportLocked_WhenTheCookiesVersionIsStale_ForIsUnlocked()
    {
        PasscodeUnlockCookie cookie = CreateCookie();
        var eventId = Guid.NewGuid();
        DefaultHttpContext writeContext = new();
        cookie.Unlock(writeContext, eventId, passcodeVersion: 1, endsAt: null, startsAt: null);
        HttpContext readContext = ReadingContextAfter(writeContext);

        Assert.False(cookie.IsUnlocked(readContext, eventId, currentPasscodeVersion: 2));
    }

    [Fact]
    public void ExpireAtLeastThirtyDaysOut_WhenNeitherDateIsSet_ForUnlock()
    {
        PasscodeUnlockCookie cookie = CreateCookie();
        DefaultHttpContext context = new();

        cookie.Unlock(context, Guid.NewGuid(), passcodeVersion: 1, endsAt: null, startsAt: null);

        DateTimeOffset expires = ExpiryOf(context);
        AssertApproximately(DateTimeOffset.UtcNow.AddDays(30), expires);
    }

    [Fact]
    public void StretchExpiryPastTheFloor_WhenEndsAtIsFarInTheFuture_ForUnlock()
    {
        PasscodeUnlockCookie cookie = CreateCookie();
        DefaultHttpContext context = new();
        DateTimeOffset endsAt = DateTimeOffset.UtcNow.AddDays(90);

        cookie.Unlock(context, Guid.NewGuid(), passcodeVersion: 1, endsAt: endsAt, startsAt: endsAt.AddDays(-2));

        DateTimeOffset expires = ExpiryOf(context);
        AssertApproximately(endsAt.AddDays(7), expires);
    }

    [Fact]
    public void ExpireAtLeastThirtyDaysOut_WhenEndsAtHasAlreadyElapsed_ForUnlock()
    {
        PasscodeUnlockCookie cookie = CreateCookie();
        DefaultHttpContext context = new();
        DateTimeOffset endsAt = DateTimeOffset.UtcNow.AddDays(-60);

        cookie.Unlock(context, Guid.NewGuid(), passcodeVersion: 1, endsAt: endsAt, startsAt: endsAt.AddDays(-2));

        DateTimeOffset expires = ExpiryOf(context);
        AssertApproximately(DateTimeOffset.UtcNow.AddDays(30), expires);
    }

    [Fact]
    public void StretchExpiryPastTheFloor_WhenOnlyStartsAtIsSet_ForUnlock()
    {
        PasscodeUnlockCookie cookie = CreateCookie();
        DefaultHttpContext context = new();
        DateTimeOffset startsAt = DateTimeOffset.UtcNow.AddDays(90);

        cookie.Unlock(context, Guid.NewGuid(), passcodeVersion: 1, endsAt: null, startsAt: startsAt);

        DateTimeOffset expires = ExpiryOf(context);
        AssertApproximately(startsAt.AddDays(7), expires);
    }

    private static PasscodeUnlockCookie CreateCookie() =>
        new(DataProtectionProvider.Create("VirtualLeadersGuide.Web.Tests"));

    private static void AssertApproximately(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True(
            Math.Abs((expected - actual).TotalSeconds) < 30,
            $"Expected an expiry near {expected:o}, got {actual:o}.");

    private static DateTimeOffset ExpiryOf(HttpContext context)
    {
        SetCookieHeaderValue parsed = SetCookieHeaderValue.ParseList([.. context.Response.Headers.SetCookie.OfType<string>()])[0];
        return parsed.Expires!.Value;
    }

    /// <remarks>
    /// Replays every <c>Set-Cookie</c> the given response wrote as one combined <c>Cookie</c> request header
    /// - a real browser sends every non-expired cookie it holds for the domain on every request, not just
    /// the one most recently set, which matters for a scenario with more than one Event's cookie in play.
    /// </remarks>
    private static HttpContext ReadingContextAfter(DefaultHttpContext writeContext, bool tamper = false)
    {
        var readContext = new DefaultHttpContext();
        var pairs = new List<string>();

        foreach (SetCookieHeaderValue parsed in SetCookieHeaderValue.ParseList([.. writeContext.Response.Headers.SetCookie.OfType<string>()]))
        {
            string value = tamper ? parsed.Value.Value! + "tampered" : parsed.Value.Value!;
            pairs.Add($"{parsed.Name}={value}");
        }

        readContext.Request.Headers.Cookie = string.Join("; ", pairs);
        return readContext;
    }
}
