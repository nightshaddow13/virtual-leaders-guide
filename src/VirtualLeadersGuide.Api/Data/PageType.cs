namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// A kind of <see cref="Page"/> content - InfoPage today (CONTEXT.md's Page entry: "has subtypes for
/// different kinds of content; only one subtype exists today").
/// </summary>
/// <remarks>
/// Not a JsonApiDotNetCore resource, same posture as <see cref="Role"/> (ADR-0017's Consequences) - purely a
/// storage-level lookup backing <see cref="Page.PageTypeId"/>, never a concept a caller queries on its own.
/// Rows are seeded via <c>HasData</c> in <see cref="VirtualLeadersGuideDbContext"/> using the well-known id in
/// <see cref="PageTypeIds"/>. See ADR-0055 for why <see cref="Page.PageTypeId"/> exists at all under TPT.
/// </remarks>
public class PageType
{
    /// <summary>This PageType's identity (see <see cref="PageTypeIds"/> for the well-known value).</summary>
    public int Id { get; set; }

    /// <summary>This PageType's display name (e.g. <c>"InfoPage"</c>).</summary>
    public required string Name { get; set; }

    /// <summary>Every <see cref="Page"/> tagged with this PageType.</summary>
    public ICollection<Page> Pages { get; set; } = new List<Page>();
}
