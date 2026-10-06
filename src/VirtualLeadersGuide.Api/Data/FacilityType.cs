using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// A freeform label for what kind of property a Facility is (CONTEXT.md's Facility Type entry) - e.g.
/// "Camp", or something custom for a one-off event held somewhere that isn't camp property at all.
/// </summary>
/// <remarks>
/// Exposed at <c>/api/facilityTypes</c> (P8-2, #166), Admin-only for every verb - see
/// <see cref="FacilityTypeResourceDefinition"/> for the enforcement. Deliberately a plain lookup
/// <see cref="Identifiable{TId}"/> with no delete/reaping lifecycle - unlike the Tab/SubTab/Section/SubSection
/// Tier ADR-0046 built for Activity/InfoPage Placement, nothing attaches to a Facility Type and it has no
/// parent to scope under, so that ADR's rationale for a lazy-create/auto-delete/never-rename entity doesn't
/// reach here. See ADR-0071.
/// </remarks>
[Resource]
public class FacilityType : Identifiable<Guid>
{
    /// <summary>This Facility Type's display name (CONTEXT.md's Facility Type entry).</summary>
    /// <remarks>
    /// The setter trims leading/trailing whitespace on assignment, matching <see cref="Facility.Name"/>'s
    /// and <see cref="Activity.Name"/>'s pattern - <c>CK_FacilityTypes_Name_NotEmpty</c>
    /// (<see cref="VirtualLeadersGuideDbContext"/>) is the backstop for anything that writes this column
    /// outside this setter. Unique (case-insensitively, via <c>IX_FacilityTypes_Name</c>) so the Web client's
    /// resolve-or-create autofill flow can't race itself into two near-duplicate rows.
    /// </remarks>
    [Attr]
    public required string Name { get; set => field = value.Trim(); }

    /// <summary>Creates a new <see cref="FacilityType"/> with the given <paramref name="name"/>.</summary>
    /// <param name="name">This Facility Type's display name.</param>
    /// <returns>The newly constructed, not-yet-persisted <see cref="FacilityType"/>.</returns>
    public static FacilityType Create(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name
    };
}
