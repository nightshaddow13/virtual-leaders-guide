namespace VirtualLeadersGuide.Api.Data;

/// <summary>A grant a <c>User</c> can hold - Admin or Director today (CONTEXT.md's Role entry, ADR-0017).</summary>
/// <remarks>
/// Not a JsonApiDotNetCore resource (ADR-0017's Consequences). Rows are seeded via <c>HasData</c> in
/// <see cref="VirtualLeadersGuideDbContext"/> using the well-known ids/names in <see cref="RoleIds"/>/
/// <see cref="RoleNames"/>.
/// </remarks>
public class Role
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public ICollection<UserRole> Grants { get; set; } = new List<UserRole>();
}
