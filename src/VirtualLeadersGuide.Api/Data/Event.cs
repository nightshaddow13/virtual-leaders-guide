namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// The top-level thing an Admin creates one per gathering (CONTEXT.md's Event entry) - has a display
/// <see cref="Name"/>, a unique URL-safe <see cref="Slug"/>, and a <see cref="Passcode"/> gating its public
/// Leaders Guide (the gate itself isn't enforced until Phase 4).
/// </summary>
/// <remarks>
/// No <c>[Resource]</c> attribute yet — stays invisible to JsonApiDotNetCore until P2-7 (#16) turns it into
/// one, the same posture <see cref="Role"/>/<see cref="UserRole"/> have (ADR-0017's Consequences).
/// </remarks>
public class Event
{
    /// <summary>The Event's primary key.</summary>
    /// <remarks>
    /// <see cref="Guid"/>, not an identity <see cref="int"/>, because <see cref="UserRole.EventId"/>'s
    /// <c>uniqueidentifier</c> column predates this type — it was left as an unenforced <c>Guid?</c>
    /// specifically because Event didn't exist yet (P2-6, #15).
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>
    /// The Event's display label (CONTEXT.md's Event entry - "Name is a display label and may repeat across
    /// Events"). The setter trims leading/trailing whitespace on assignment, so the in-memory value never
    /// disagrees with what's persisted; the database's <c>CK_Events_Name_NotEmpty</c> constraint is the
    /// backstop for anything that writes this column outside this setter.
    /// </summary>
    /// <remarks>
    /// Unique via a plain (non-filtered) index, deliberately — not an oversight. CONTEXT.md's Event/Slug
    /// entries call out that Name stops being globally unique once a future archiving feature ships (an
    /// archived Event and a new Event could then share a Name); that feature is out of scope here (P2-6,
    /// #15), so a plain unique index is correct for right now.
    /// </remarks>
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>
    /// The URL-safe route key for this Event's public Leaders Guide (<c>yourdomain.com/e/{slug}</c>,
    /// ADR-0005).
    /// </summary>
    /// <remarks>
    /// Auto-derived from <see cref="Name"/> via <see cref="SlugDerivation.From"/> as a starting value, but
    /// editable afterward (CONTEXT.md's Slug entry). Always lowercase - the setter normalizes on assignment
    /// (not just on save) so route resolution stays unambiguous regardless of how it was typed, and so the
    /// in-memory value never disagrees with what's persisted. The database charset CHECK constraint
    /// (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for anything that writes this column
    /// outside this setter.
    /// </remarks>
    public required string Slug { get; set => field = value.ToLowerInvariant(); }

    /// <summary>
    /// The shared secret a visitor enters to unlock read access to this Event's Leaders Guide (CONTEXT.md's
    /// Passcode entry).
    /// </summary>
    /// <remarks>
    /// Never blank - generate one with <see cref="PasscodeGenerator.Generate"/> when constructing a new
    /// Event; nothing populates this automatically on your behalf (see that type's remarks for why). Stored
    /// encrypted-at-rest (ADR-0009, ADR-0026) via a <see cref="DataProtectionStringConverter"/> - see that
    /// type's remarks for why a DB constraint can't validate this column's plaintext shape the way
    /// <see cref="Name"/> and <see cref="Slug"/> are validated.
    /// </remarks>
    public required string Passcode { get; set; }

    /// <summary>The Director (and future Event-scoped role) grants scoped to this Event.</summary>
    public ICollection<UserRole> RoleGrants { get; set; } = new List<UserRole>();

    /// <summary>
    /// Creates a new <see cref="Event"/> with a fresh <see cref="Id"/>, <paramref name="name"/>, a
    /// <see cref="Slug"/> auto-derived from <paramref name="name"/> via <see cref="SlugDerivation.From"/> when
    /// <paramref name="slug"/> is omitted (the AC this ticket, P2-6/#15, is named for), and a freshly
    /// generated <see cref="Passcode"/>.
    /// </summary>
    /// <param name="name">The Event's display label.</param>
    /// <param name="slug">
    /// An explicit Slug to use instead of the one derived from <paramref name="name"/> - omit to get the
    /// auto-derived starting value the acceptance criteria describes. Callers editing an existing Event's Slug
    /// afterward just assign <see cref="Slug"/> directly rather than calling this factory again.
    /// </param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="Event"/>.</returns>
    public static Event Create(string name, string? slug = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug ?? SlugDerivation.From(name),
        Passcode = PasscodeGenerator.Generate()
    };
}
