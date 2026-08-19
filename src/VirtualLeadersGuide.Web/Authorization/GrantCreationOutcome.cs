namespace VirtualLeadersGuide.Web.Authorization;

/// <summary>Outcomes <see cref="ApiRoleGrantClient.CreateGrantAsync"/> distinguishes.</summary>
public enum GrantCreationOutcome
{
    Created,
    AlreadyGranted,
    UserOrRoleNotFound
}
