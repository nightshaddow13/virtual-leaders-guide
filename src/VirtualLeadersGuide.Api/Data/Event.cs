namespace VirtualLeadersGuide.Api.Data;

// Guid primary key is mandated by UserRole.EventId's existing uniqueidentifier column - that column predates
// this type and was left as an unenforced Guid? specifically because Event didn't exist yet (P2-6, #15); see
// UserRole.cs for the FK now added against this entity.
//
// No [Resource] attribute - Event stays invisible to JsonApiDotNetCore until P2-7 (#16) turns it into a
// resource, same posture Role/UserRole have today (see DomainAuthorizationEntitiesAreNotJsonApiResourcesShould).
//
// No IsArchived column and Name's uniqueness below is a plain (non-filtered) index - both deliberately out of
// scope for this ticket. CONTEXT.md's Event/Slug entries call out that Name stops being globally unique once a
// future archiving feature ships (an archived Event and a new Event could then share a Name); this ticket does
// not build that feature, so a plain unique index is correct for right now, not an oversight.
/// <summary>
/// The top-level thing an Admin creates one per gathering (CONTEXT.md's Event entry) - has a display
/// <see cref="Name"/>, a unique URL-safe <see cref="Slug"/>, and a <see cref="Passcode"/> gating its public
/// Leaders Guide (the gate itself isn't enforced until Phase 4).
/// </summary>
public class Event
{
    /// <summary>The Event's primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The Event's display label (CONTEXT.md's Event entry - "Name is a display label and may repeat across
    /// Events"). Unique for now via a plain index; see this file's rationale comment for why that's correct at
    /// this phase rather than a gap. The setter trims leading/trailing whitespace on assignment, so the
    /// in-memory value never disagrees with what's persisted; the database's
    /// <c>CK_Events_Name_NotEmpty</c> constraint is the backstop for anything that writes this column outside
    /// this setter.
    /// </summary>
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>
    /// The URL-safe route key for this Event's public Leaders Guide (<c>yourdomain.com/e/{slug}</c>,
    /// ADR-0005). Auto-derived from <see cref="Name"/> via <see cref="Slug.From"/> as a starting value, but
    /// editable afterward (CONTEXT.md's Slug entry). Always lowercase - the setter normalizes on assignment
    /// (not just on save) so route resolution stays unambiguous regardless of how it was typed, and so the
    /// in-memory value never disagrees with what's persisted. The database charset CHECK constraint
    /// (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for anything that writes this column
    /// outside this setter.
    /// </summary>
    public required string Slug { get; set => field = value.ToLowerInvariant(); }

    /// <summary>
    /// The shared secret a visitor enters to unlock read access to this Event's Leaders Guide (CONTEXT.md's
    /// Passcode entry). Never blank - generate one with <see cref="PasscodeGenerator.Generate"/> when
    /// constructing a new Event; nothing populates this automatically on your behalf (see that type's remarks
    /// for why). Stored encrypted-at-rest (ADR-0009, ADR-0026) via a
    /// <see cref="DataProtectionStringConverter"/> - see that type's remarks for why a DB constraint can't
    /// validate this column's plaintext shape the way <see cref="Name"/> and <see cref="Slug"/> are validated.
    /// </summary>
    public required string Passcode { get; set; }

    /// <summary>The Director (and future Event-scoped role) grants scoped to this Event.</summary>
    public ICollection<UserRole> RoleGrants { get; set; } = new List<UserRole>();

    /// <summary>
    /// Creates a new <see cref="Event"/> with a fresh <see cref="Id"/>, <paramref name="name"/>, a
    /// <see cref="Slug"/> auto-derived from <paramref name="name"/> via <see cref="Slug.From"/> when
    /// <paramref name="slug"/> is omitted (the AC this ticket, P2-6/#15, is named for), and a freshly
    /// generated <see cref="Passcode"/>.
    /// </summary>
    /// <param name="name">The Event's display label.</param>
    /// <param name="slug">
    /// An explicit Slug to use instead of the one derived from <paramref name="name"/> - omit to get the
    /// auto-derived starting value the acceptance criteria describes. Callers editing an existing Event's Slug
    /// afterward just assign <see cref="Slug"/> directly rather than calling this factory again.
    /// </param>
    public static Event Create(string name, string? slug = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        // Fully qualified - "Slug" unqualified inside this class body resolves to the instance property
        // above, not the Slug static class, even in this static method (simple-name lookup favors the
        // enclosing type's own members over other types in scope).
        Slug = slug ?? VirtualLeadersGuide.Api.Data.Slug.From(name),
        Passcode = PasscodeGenerator.Generate()
    };
}
