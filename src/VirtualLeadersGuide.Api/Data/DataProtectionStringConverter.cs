using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// An EF Core <see cref="ValueConverter{TModel,TProvider}"/> that encrypts a string column at rest via
/// ASP.NET Core's Data Protection API (ADR-0009, ADR-0026) - plaintext in the domain model, Data Protection
/// ciphertext in the database column.
/// </summary>
/// <remarks>
/// <see cref="IDataProtector.Protect"/> produces non-deterministic ciphertext (a fresh IV each call), so
/// <c>Where(e =&gt; e.Passcode == someValue)</c> can never match even when <c>someValue</c> is the right
/// plaintext - Phase 4's public gate must load the <see cref="Event"/> by <see cref="Event.Slug"/> and
/// compare the decrypted Passcode in memory, never push the comparison into a query. This is also why no DB
/// CHECK constraint can validate Passcode's plaintext shape (non-empty, length, charset) the way
/// <see cref="Event.Name"/> and <see cref="Event.Slug"/> are validated - the column only ever holds
/// ciphertext, never the value a constraint would need to inspect. That invariant is upheld by
/// <see cref="PasscodeGenerator"/> instead, at the point Passcode is assigned.
/// </remarks>
public sealed class DataProtectionStringConverter : ValueConverter<string, string>
{
    /// <summary>Creates the converter using an already-purposed <see cref="IDataProtector"/>.</summary>
    /// <param name="protector">
    /// The already-purposed protector to encrypt/decrypt this column's values with (see
    /// <see cref="IDataProtectionProvider.CreateProtector(string)"/> at the call site for how it was purposed).
    /// </param>
    public DataProtectionStringConverter(IDataProtector protector)
        : base(plaintext => protector.Protect(plaintext), ciphertext => protector.Unprotect(ciphertext))
    {
    }
}
