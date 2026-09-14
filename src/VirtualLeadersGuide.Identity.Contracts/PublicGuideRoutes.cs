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

    public static string ForEventBySlug(string slug) =>
        $"{GroupPrefix}/events/{Uri.EscapeDataString(slug)}";

    public static string ForPasscodeCheck(string slug) =>
        $"{GroupPrefix}/events/{Uri.EscapeDataString(slug)}/passcode";
}
