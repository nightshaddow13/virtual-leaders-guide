using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// The top-level thing an Admin creates one per gathering (CONTEXT.md's Event entry) - has a display
/// <see cref="Name"/>, a unique URL-safe <see cref="Slug"/>, and a <see cref="Passcode"/> gating its public
/// Leaders Guide (the gate itself isn't enforced until Phase 4).
/// </summary>
/// <remarks>
/// Exposed at <c>/api/events</c> (P2-7, #16) - see <see cref="EventResourceDefinition"/> for the
/// Admin/Director scoping enforced on top of the CRUD this attribute generates. <see cref="Role"/> and
/// <see cref="UserRole"/> stay unexposed (ADR-0017's Consequences).
/// </remarks>
[Resource]
public class Event : Identifiable<Guid>
{
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
    /// #15), so a plain unique index is correct for right now. <see cref="EventResourceDefinition"/> maps a
    /// collision on this index to a 409, not the 500 the raw index violation would otherwise produce - see
    /// its remarks for why this check is a provisional stand-in, unlike <see cref="Slug"/>'s.
    /// </remarks>
    [Attr]
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>
    /// The URL-safe route key for this Event's public Leaders Guide (<c>yourdomain.com/e/{slug}</c>,
    /// ADR-0005).
    /// </summary>
    /// <remarks>
    /// Auto-derived from <see cref="Name"/> via <see cref="SlugDerivation.From"/> as a starting value, but
    /// editable afterward (CONTEXT.md's Slug entry) - not creatable over <c>/api/events</c>
    /// (<see cref="AttrCapabilities.AllowCreate"/> deliberately absent below): a POST always gets the
    /// auto-derived value (<see cref="EventResourceDefinition"/>), and a client-chosen Slug is set via a
    /// follow-up PATCH instead, matching "starting value ... edit afterward" literally. Always lowercase -
    /// the setter normalizes on assignment (not just on save) so route resolution stays unambiguous
    /// regardless of how it was typed, and so the in-memory value never disagrees with what's persisted. The
    /// database charset CHECK constraint (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for
    /// anything that writes this column outside this setter. This uniqueness is permanent (it's the route
    /// key), unlike <see cref="Name"/>'s - see <see cref="EventResourceDefinition"/>'s remarks.
    /// </remarks>
    /// <remarks>
    /// Typed <c>string?</c>, not non-nullable - the column itself stays <c>NOT NULL</c>
    /// (<see cref="VirtualLeadersGuideDbContext"/> configures this explicitly via <c>IsRequired()</c> rather
    /// than relying on the convention a non-nullable C# type would otherwise trigger). Non-nullable here would
    /// make ASP.NET Core's model validation implicitly require this attribute on every <c>POST</c>, which
    /// directly contradicts the "not creatable" capability above - see <see cref="EventResourceDefinition"/>'s
    /// remarks.
    /// </remarks>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowChange
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required string? Slug { get; set => field = value?.ToLowerInvariant(); }

    /// <summary>
    /// The shared secret a visitor enters to unlock read access to this Event's Leaders Guide (CONTEXT.md's
    /// Passcode entry).
    /// </summary>
    /// <remarks>
    /// Never blank - generate one with <see cref="PasscodeGenerator.Generate"/> when constructing a new
    /// Event; nothing populates this automatically on your behalf (see that type's remarks for why). Stored
    /// encrypted-at-rest (ADR-0009, ADR-0026) via a <see cref="DataProtectionStringConverter"/> - see that
    /// type's remarks for why a DB constraint can't validate this column's plaintext shape the way
    /// <see cref="Name"/> and <see cref="Slug"/> are validated. Viewable and editable over <c>/api/events</c>
    /// (CONTEXT.md: "editable by an Admin/Director") but not creatable
    /// (<see cref="AttrCapabilities.AllowCreate"/> deliberately absent below) - a client never invents its
    /// own Passcode; a POST always gets a freshly generated one (<see cref="EventResourceDefinition"/>),
    /// matching CONTEXT.md's "auto-generated the moment an Event is created". Not filterable/sortable either
    /// - it's ciphertext at rest, so a filter or sort on it would silently never match.
    /// </remarks>
    /// <remarks>
    /// Typed <c>string?</c>, not non-nullable, for the same reason as <see cref="Slug"/>'s - see that
    /// property's remarks.
    /// </remarks>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowChange)]
    public required string? Passcode { get; set; }

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
