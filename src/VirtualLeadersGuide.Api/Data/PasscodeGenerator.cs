using PasswordGenerator;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Generates a fresh <see cref="Event.Passcode"/> value: two Title-cased words from the EFF Large Wordlist
/// concatenated with no separator (e.g. <c>TigerLantern</c>) - memorable enough for a visitor to read off a
/// printed handout and type by hand, rather than an opaque random token.
/// </summary>
/// <remarks>
/// See ADR-0027 for why a memorable word pair rather than a hand-curated list or an opaque token. Not a
/// property initializer on <see cref="Event.Passcode"/> - EF Core constructs an <see cref="Event"/> via
/// <c>new Event()</c> every time it materializes a row from a query, before overwriting properties with the
/// persisted values. A property initializer here would silently burn a cryptographically-random passphrase
/// generation on every single row read, for a value immediately discarded. Callers (<see cref="Event.Create"/>)
/// must call <see cref="Generate"/> explicitly instead, same pattern as <c>SlugDerivation.From</c>.
/// </remarks>
public static class PasscodeGenerator
{
    /// <summary>Generates a new random two-word Passcode.</summary>
    /// <returns>A freshly generated, two-word Passcode value (e.g. <c>TigerLantern</c>).</returns>
    /// <remarks>
    /// <c>PassphraseGenerator</c> (<c>ForPassphrase</c>'s concrete runtime type) implements
    /// <see cref="IDisposable"/> even though the <c>IPasswordGenerator</c> interface it's returned as
    /// doesn't declare it - cast defensively rather than assume every implementation needs disposal.
    /// </remarks>
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
            (generator as IDisposable)?.Dispose();
        }
    }
}
