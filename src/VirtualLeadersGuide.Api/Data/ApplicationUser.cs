using Microsoft.AspNetCore.Identity;

namespace VirtualLeadersGuide.Api.Data;

// The ASP.NET Core Identity credential row - password hash, security stamp, lockout state. Distinct from
// the domain User (P2-3, #12): a name/email/Role-holding row that can exist before this one does (e.g. an
// Invite creates a pending User with no ApplicationUser yet). See CONTEXT.md's User/Credential entries and
// ADR-0022. No extra properties yet - the link back to the domain User row is ADR-0017's repurposed
// nullable credential field, added when P2-3 lands.
public class ApplicationUser : IdentityUser;
