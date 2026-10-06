using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// A physical property where Events happen (CONTEXT.md's Facility entry) - a camp, retreat center, or any
/// other venue, created and managed independently of any Event and reused across as many Events as reuse it.
/// Real and listable before it has any Program Areas, Buildings, Campsites, or assigned Events (P8-2, #166's
/// AC) - those are later Phase 8+ concerns.
/// </summary>
/// <remarks>
/// Exposed at <c>/api/facilities</c> (P8-2, #166) - unlike every Event-scoped resource so far (Activity,
/// InfoPage), a Facility carries no <c>EventId</c> at all (ADR-0066): it has no owning Event for a Director
/// Grant to apply to. Read is open to any signed-in caller (Admin or Director); Write is Admin-only -
/// see <see cref="FacilityAccessPolicy"/> for the enforcement and ADR-0070 for why the split lands there.
/// </remarks>
[Resource]
public class Facility : Identifiable<Guid>
{
    /// <summary>This Facility's display name (CONTEXT.md's Facility entry).</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Activity.Name"/>'s
    /// and <see cref="Data.Event.Name"/>'s pattern - <c>CK_Facilities_Name_NotEmpty</c>
    /// (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for anything that writes this column
    /// outside this setter.
    /// </remarks>
    [Attr]
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>
    /// The <see cref="Data.FacilityType.Id"/> this Facility is tagged with - required (CONTEXT.md's Facility
    /// entry: "Has a Name and a Facility Type"), and unlike <see cref="Activity.EventId"/> this FK **can**
    /// change (<see cref="AttrCapabilities.AllowChange"/>) - a later story edits it.
    /// </summary>
    [Attr(Capabilities = AttrCapabilities.AllowView | AttrCapabilities.AllowCreate | AttrCapabilities.AllowChange
        | AttrCapabilities.AllowFilter | AttrCapabilities.AllowSort)]
    public required Guid FacilityTypeId { get; set; }

    /// <summary>
    /// The Facility Type this Facility is tagged with. Not <c>[HasOne]</c> - <see cref="FacilityTypeId"/> is a
    /// flat attribute, not a JSON:API relationship, mirroring <see cref="Activity.EventId"/>/<see cref="Page.EventId"/>'s
    /// convention so the Web client never needs <c>?include=</c>.
    /// </summary>
    public FacilityType? FacilityType { get; set; }

    /// <summary>Creates a new <see cref="Facility"/> with the given <paramref name="name"/> and <paramref name="facilityTypeId"/>.</summary>
    /// <param name="name">This Facility's display name.</param>
    /// <param name="facilityTypeId">The <see cref="Data.FacilityType"/> this Facility is tagged with.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="Facility"/>.</returns>
    public static Facility Create(string name, Guid facilityTypeId) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        FacilityTypeId = facilityTypeId
    };
}
