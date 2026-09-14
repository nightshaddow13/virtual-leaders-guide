namespace VirtualLeadersGuide.Identity.Contracts;

/// <summary>The request body for <c>PublicGuideRoutes.PasscodeCheck</c> (P4-2, #72).</summary>
public sealed class PasscodeCheckRequest
{
    /// <summary>
    /// The visitor's typed guess, exactly as entered - normalization (trim, case fold, strip internal
    /// whitespace) happens Api-side against the decrypted Passcode, not here.
    /// </summary>
    public required string Passcode { get; set; }
}
