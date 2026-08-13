using PasswordGenerator;

namespace VirtualLeadersGuide.Api.Data;

// Uses the PasswordGenerator NuGet package's EFF-large-wordlist-backed passphrase generator rather than a
// hand-curated word list - CONTEXT.md's Passcode entry describes visitors reading this off a printed handout
// and typing it in, so it needs to be memorable, not high-entropy; two words (~1,000,000 combinations) is
// already well past what this threat model (ADR-0009's "keep drive-by access out," not a real auth system)
// needs. See ADR-0027 for the full trade-off.
//
// Not a property initializer on Event.Passcode - EF Core constructs an Event via `new Event()` every time it
// materializes a row from a query, before overwriting properties with the persisted values. A property
// initializer here would silently burn a cryptographically-random passphrase generation on every single row
// read, for a value immediately discarded. Callers (Event.Create) must call Generate() explicitly instead,
// same pattern as SlugDerivation.From.
/// <summary>
/// Generates a fresh <see cref="Event.Passcode"/> value: two Title-cased words from the EFF Large Wordlist
/// concatenated with no separator (e.g. <c>TigerLantern</c>) - memorable enough for a visitor to read off a
/// printed handout and type by hand, rather than an opaque random token.
/// </summary>
public static class PasscodeGenerator
{
    /// <summary>Generates a new random two-word Passcode.</summary>
    /// <returns>A freshly generated, two-word Passcode value (e.g. <c>TigerLantern</c>).</returns>
    public static string Generate()
    {
        IPasswordGenerator generator = Password.ForPassphrase(
            words: 2, separator: null, capitalize: true, includeNumber: false, includeSymbol: false);
        try
        {
            return generator.Next();
        }
        finally
        {
            // PassphraseGenerator (ForPassphrase's concrete runtime type) implements IDisposable even though
            // the IPasswordGenerator interface it's returned as doesn't declare Dispose - cast defensively
            // rather than assume every implementation needs it.
            (generator as IDisposable)?.Dispose();
        }
    }
}
