namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// Route shape shared between Api's <c>PublicGuideEndpoints</c> (which maps these) and Web's
/// <c>PublicEventClient</c> (which calls them), mirroring <see cref="InternalAuthorizationRoutes"/>'
/// pattern so the two sides can't drift.
/// </summary>
/// <remarks>
/// Deliberately outside JsonApi's <c>/api</c> namespace and gated only by the <c>X-Internal-Key</c> fallback
/// policy (ADR-0015's amendment), not <c>RequireInternalUser</c> - an anonymous visitor calling this through
/// Web has no signed-in user, and therefore no internal JWT to send (P4-2, #72). <c>{slug}</c> is
/// <c>Event.Slug</c> (already lowercase - see that property's remarks), never an <c>Event.Id</c> - a visitor
/// arrives with an address, not a Guid.
/// </remarks>
public static class PublicGuideRoutes
{
    public const string GroupPrefix = "/internal/public";

    public const string EventBySlug = "/events/{slug}";

    public const string PasscodeCheck = "/events/{slug}/passcode";

    /// <summary>The route, under <see cref="GroupPrefix"/>, that returns an Event's whole guide.</summary>
    public const string GuideByEvent = "/events/{slug}/guide";

    public static string ForEventBySlug(string slug) =>
        $"{GroupPrefix}/events/{Uri.EscapeDataString(slug)}";

    public static string ForPasscodeCheck(string slug) =>
        $"{GroupPrefix}/events/{Uri.EscapeDataString(slug)}/passcode";

    /// <summary>The path a caller requests for <see cref="GuideByEvent"/>.</summary>
    /// <param name="slug">The Event's Slug, escaped here.</param>
    /// <returns>The path, including <see cref="GroupPrefix"/>.</returns>
    public static string ForGuideByEvent(string slug) =>
        $"{GroupPrefix}/events/{Uri.EscapeDataString(slug)}/guide";
}
