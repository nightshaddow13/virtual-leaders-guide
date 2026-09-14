namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>
/// The public, anonymous-safe view of an Event, returned by
/// <c>PublicGuideRoutes.EventBySlug</c>/<c>PasscodeCheck</c> (P4-2, #72).
/// </summary>
/// <remarks>
/// Deliberately excludes <c>Event.Passcode</c> - this DTO is what an anonymous visitor's request gets back,
/// and the whole point of the passcode-check endpoint is that Web never receives the plaintext or ciphertext,
/// only whether a submitted guess matched. <see cref="Status"/> is carried as a <c>string</c>, not an enum -
/// Api and Web each keep their own <c>EventStatus</c> enum already, and this contract deliberately doesn't
/// start a third shared one just to avoid one more <c>Enum.Parse</c> at the boundary.
/// </remarks>
public sealed class PublicEventDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    /// <summary>The Event's *effective* Status - an elapsed <c>Live</c> row already reads <c>Past</c> here.</summary>
    public required string Status { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>
    /// The generation of the Event's Passcode currently in effect (<c>Event.PasscodeVersion</c>) - what a
    /// visitor's Unlock cookie is checked against (ADR-0057). An ordinary visible integer, not
    /// secret-derived, so returning it here widens no exposure.
    /// </summary>
    public required int PasscodeVersion { get; set; }
}
