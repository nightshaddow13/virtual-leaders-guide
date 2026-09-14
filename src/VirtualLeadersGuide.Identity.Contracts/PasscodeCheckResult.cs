namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>The response body for <c>PublicGuideRoutes.PasscodeCheck</c> on a <c>200</c> (P4-2, #72).</summary>
/// <remarks>
/// Always <c>200</c> with <see cref="Matched"/> set, never a <c>401</c>/<c>403</c> for a wrong guess - a
/// mismatched Passcode is an expected, ordinary outcome of this endpoint, not an authorization failure. Only
/// an unknown Slug or a <c>Draft</c> Event (no public existence yet) gets a <c>404</c> instead of a body at
/// all - see <c>PublicGuideEndpoints</c>.
/// </remarks>
public sealed class PasscodeCheckResult
{
    public required bool Matched { get; set; }

    /// <summary><see langword="null"/> when <see cref="Matched"/> is <see langword="false"/>.</summary>
    public Guid? EventId { get; set; }

    /// <summary>
    /// The Event's <c>PasscodeVersion</c> at the moment of this check - <see langword="null"/> when
    /// <see cref="Matched"/> is <see langword="false"/>. What Web stamps into the new Unlock cookie
    /// (ADR-0057).
    /// </summary>
    public int? PasscodeVersion { get; set; }
}
