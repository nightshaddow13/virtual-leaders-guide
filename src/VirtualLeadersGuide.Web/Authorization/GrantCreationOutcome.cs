namespace VirtualLeadersGuide.Web.Authorization;

// Outcomes ApiRoleGrantClient.CreateGrantAsync distinguishes - see that method's comment for why
// AlreadyGranted is a typed outcome rather than an exception.
public enum GrantCreationOutcome
{
    Created,
    AlreadyGranted,
    UserOrRoleNotFound
}
