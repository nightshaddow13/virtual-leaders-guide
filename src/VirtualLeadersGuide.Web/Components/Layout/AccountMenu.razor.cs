using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Components.Layout;

/// <summary>The signed-in person's menu in the top bar: email, role, Manage account and Sign out.</summary>
/// <remarks>Assumes nothing about being staff - a signed-in person may hold no Role at all (#44).</remarks>
public partial class AccountMenu
{
    /// <summary>The signed-in person.</summary>
    [Parameter, EditorRequired]
    public ClaimsPrincipal User { get; set; } = default!;

    /// <remarks>
    /// Admin outranks Director when a user holds both, so this always shows the higher-privilege role rather
    /// than an arbitrary one. Claim values are <see cref="RoleClaimValue.Format"/> output (<c>"Admin"</c> or
    /// <c>"Director:{eventId}"</c>) - the Event scope is parsed off, never displayed here.
    /// </remarks>
    private string DisplayRole
    {
        get
        {
            string[] roleNames = [.. User.FindAll(ClaimTypes.Role)
                .Select(claim => RoleClaimValue.TryParse(claim.Value, out string name, out _) ? name : null)
                .OfType<string>()
                .Distinct()];

            return roleNames.FirstOrDefault(name => name == RoleNames.Admin) ?? roleNames.FirstOrDefault() ?? "No role";
        }
    }

    private string DisplayEmail => User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name ?? string.Empty;
}
