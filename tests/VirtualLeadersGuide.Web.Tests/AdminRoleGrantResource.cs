using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Tests;

/// <summary>
/// The JSON:API <c>roleGrants</c> resource for an Admin grant - shared by <see cref="DirectorInviteServiceShould"/>
/// and <see cref="UserDetailShould"/>, both of which stub an Admin-target lookup for their own
/// <c>DeleteAsync</c>/Danger-zone coverage (P2-19, #114). <c>ApiDirectorClientShould</c>/<c>EventEditorShould</c>
/// each keep their own older, differently-shaped <c>GrantResource</c> private helper - this one is deliberately
/// narrow (Admin-only, no <c>eventId</c>) rather than a general-purpose replacement for those.
/// </summary>
internal static class AdminRoleGrantResource
{
    public static object[] ForUser(string userId) =>
        [new { type = "roleGrants", id = Guid.NewGuid().ToString(), attributes = new { userId, roleId = RoleIds.Admin, eventId = (Guid?)null } }];
}
