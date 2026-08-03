namespace VirtualLeadersGuide.Api.Data;

// A grant a User can hold - Admin or Director today (see CONTEXT.md's Role entry, ADR-0017). Not a
// JsonApiDotNetCore resource: not Identifiable<T>, so it's invisible to /api like every other entity in
// this file until a ticket deliberately opts it in. Rows are seeded via HasData in
// VirtualLeadersGuideDbContext.OnModelCreating using the well-known ids/names in RoleIds/RoleNames.
public class Role
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public ICollection<UserRole> Grants { get; set; } = new List<UserRole>();
}
