using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VirtualLeadersGuide.Api.Data;

// First ValueConverter in this repo (HasConversion had zero prior usages) - Event.Passcode is the only column
// that needs one, so precedent starts here.
//
// IDataProtector.Protect produces non-deterministic ciphertext (a fresh IV each call), so
// `Where(e => e.Passcode == someValue)` can never match even when someValue is the right plaintext - Phase 4's
// public gate must load the Event by Slug and compare the decrypted Passcode in memory, never push the
// comparison into a query. This is also why no DB CHECK constraint can validate Passcode's plaintext shape
// (non-empty, length, charset) the way Name and Slug are validated in VirtualLeadersGuideDbContext - the
// column only ever holds ciphertext, never the value a constraint would need to inspect. That invariant is
// upheld by PasscodeGenerator instead, at the point Passcode is assigned.
/// <summary>
/// An EF Core <see cref="ValueConverter{TModel,TProvider}"/> that encrypts a string column at rest via
/// ASP.NET Core's Data Protection API (ADR-0009, ADR-0026) - plaintext in the domain model, Data Protection
/// ciphertext in the database column.
/// </summary>
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
