namespace VirtualLeadersGuide.Api.Data;

// The grant itself - a User holding a Role, optionally scoped to an Event (ADR-0017). A null EventId is
// platform-wide (Admin); a set EventId scopes the grant to that Event (Director, and future Event-scoped
// roles). UserId is ApplicationUser.Id (string) - there is no separate domain User row; ADR-0024
// supersedes ADR-0017's three-table design once ADR-0019 brought credentials in-house.
//
// EventId is deliberately an unenforced Guid? with no navigation/FK: Event doesn't exist yet (P2-6, #15).
// P2-6 must give Event a Guid primary key to match this column - see the comment left on that issue.
// Like Role, not a JsonApiDotNetCore resource yet - P2-8 (#17) is what turns this into one.
public class UserRole
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public int RoleId { get; set; }

    public Role? Role { get; set; }

    public Guid? EventId { get; set; }
}
