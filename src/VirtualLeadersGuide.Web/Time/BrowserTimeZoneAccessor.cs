using Microsoft.JSInterop;

namespace VirtualLeadersGuide.Web.Time;

/// <summary>
/// Resolves the signed-in visitor's browser timezone (P2-15, #102) - the reference frame
/// <c>EventDateRange</c> renders <c>Event.StartsAt</c>/<c>Event.EndsAt</c> in for that visitor, and the
/// frame an Admin's typed Start/End is converted from before it's sent to Api.
/// </summary>
/// <remarks>
/// This app's first <see cref="IJSRuntime"/> consumer - Blazor Server runs the circuit on the server, so
/// there's no other way to learn what timezone the browser itself is in. Scoped, one instance per circuit,
/// and caches its result for that circuit's lifetime: the browser's timezone doesn't change mid-session, so
/// only the first caller on a page pays the JS round trip. Falls back to <see cref="TimeZoneInfo.Utc"/> when
/// interop isn't available yet (called before the circuit has finished connecting - see <c>EventEditor</c>
/// and <c>Dashboard</c>'s <c>OnAfterRenderAsync</c> for why both defer the call to <c>firstRender</c>), when
/// the call returns no id at all (bUnit's <c>JSRuntimeMode.Loose</c> returns <see langword="null"/> for any
/// unconfigured invocation - <c>DashboardRenderingShould</c>/<c>EventEditorShould</c> exercise exactly this
/// path, per ADR-0041's stated bUnit/JS-interop exemption), or when the reported id doesn't resolve; a
/// viewer temporarily seeing UTC times is a far smaller failure than the page crashing outright.
/// <c>wwwroot/js/browser-timezone.js</c> is the JS half - a single global function, not a Blazor-specific
/// module, matching <c>theme-toggle.js</c>'s shape.
/// </remarks>
public sealed class BrowserTimeZoneAccessor(IJSRuntime jsRuntime)
{
    private TimeZoneInfo? _cached;

    /// <summary>
    /// Returns the browser's <see cref="TimeZoneInfo"/>, resolving and caching it on first call this circuit.
    /// </summary>
    /// <returns>
    /// The browser's timezone, or <see cref="TimeZoneInfo.Utc"/> if it couldn't be determined - never throws.
    /// </returns>
    public async Task<TimeZoneInfo> GetTimeZoneAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        TimeZoneInfo resolved = TimeZoneInfo.Utc;
        try
        {
            string? id = await jsRuntime.InvokeAsync<string>("vlgGetTimeZone");
            if (!string.IsNullOrEmpty(id))
            {
                resolved = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
            or TimeZoneNotFoundException or InvalidTimeZoneException or TaskCanceledException)
        {
            resolved = TimeZoneInfo.Utc;
        }

        _cached = resolved;
        return resolved;
    }
}
