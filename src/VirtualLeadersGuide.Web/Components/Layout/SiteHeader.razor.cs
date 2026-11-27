using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;

namespace VirtualLeadersGuide.Web.Components.Layout;

/// <summary>
/// The one top bar every page shares (P2-26, #241): the logo, the navigation for where you are, the theme
/// toggle, and Sign in or the account menu.
/// </summary>
/// <remarks>
/// The navigation is the management links for staff, except on a guide, where everyone - staff included - gets
/// that guide's Tabs, supplied by the page through a section (<see cref="PublicGuide.GuideNav.HeaderSection"/>).
/// </remarks>
public partial class SiteHeader
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private enum Section { None, Dashboard, Facilities, Users }

    private string Path => NavigationManager.ToBaseRelativePath(NavigationManager.Uri).Split('?', '#')[0];

    private bool IsOnAGuide => Path.StartsWith("e/", StringComparison.OrdinalIgnoreCase);

    private Section CurrentSection => Path.ToLowerInvariant().Split('/') switch
    {
        ["dashboard", "facilities", ..] => Section.Facilities,
        ["dashboard", "users", ..] => Section.Users,
        ["dashboard", ..] => Section.Dashboard,
        _ => Section.None
    };

    private string? Current(Section section) => CurrentSection == section ? "page" : null;
}
