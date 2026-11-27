using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Components.Layout;

namespace VirtualLeadersGuide.Web.Tests;

public class SiteHeaderShould : BunitContext
{
    private readonly BunitAuthorizationContext _auth;

    public SiteHeaderShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _auth = this.AddAuthorization();
    }

    [Fact]
    public void OfferOnlySignIn_WhenSignedOut_ForRender()
    {
        IRenderedComponent<SiteHeader> cut = RenderAt("about");

        Assert.Equal("Sign in", cut.Find("a[href='Account/Login']").TextContent.Trim());
        Assert.Empty(cut.FindAll("nav"));
        Assert.Empty(cut.FindAll("details"));
    }

    [Fact]
    public void OfferEveryManagementLink_WhenSignedInAsAnAdmin_ForRender()
    {
        SignInAs(RoleNames.Admin);

        IRenderedComponent<SiteHeader> cut = RenderAt("dashboard/users/abc");

        Assert.Equal(["Dashboard", "Facilities", "Users"], MainLinks(cut));
        Assert.Equal("Users", cut.Find("nav[aria-label='Main'] a[aria-current]").TextContent);
        Assert.Contains("Manage account", cut.Find("nav[aria-label='Account']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void OfferOnlyTheDashboard_WhenSignedInAsADirector_ForRender()
    {
        SignInAs(RoleNames.Director);

        IRenderedComponent<SiteHeader> cut = RenderAt("dashboard");

        Assert.Equal(["Dashboard"], MainLinks(cut));
    }

    [Fact]
    public void OfferTheAccountMenuButNoManagementLinks_WhenSignedInWithNoRole_ForRender()
    {
        _auth.SetAuthorized("someone@example.test");

        IRenderedComponent<SiteHeader> cut = RenderAt("about");

        Assert.Empty(cut.FindAll("nav[aria-label='Main']"));
        Assert.NotNull(cut.Find("nav[aria-label='Account']"));
    }

    [Fact]
    public void HideManagementLinksButKeepTheAccountMenu_WhenAnAdminIsOnAGuide_ForRender()
    {
        SignInAs(RoleNames.Admin);

        IRenderedComponent<SiteHeader> cut = RenderAt("e/summer-camporee?tab=Day");

        Assert.Empty(cut.FindAll("nav[aria-label='Main']"));
        Assert.NotNull(cut.Find("nav[aria-label='Account']"));
    }

    [Fact]
    public void OfferSignIn_WhenSignedOutOnAGuide_ForRender()
    {
        IRenderedComponent<SiteHeader> cut = RenderAt("e/summer-camporee");

        Assert.NotNull(cut.Find("a[href='Account/Login']"));
        Assert.Empty(cut.FindAll("nav[aria-label='Main']"));
    }

    private static IEnumerable<string> MainLinks(IRenderedComponent<SiteHeader> cut) =>
        cut.FindAll("nav[aria-label='Main'] a").Select(a => a.TextContent);

    private void SignInAs(string role)
    {
        _auth.SetAuthorized("staff@example.test");
        _auth.SetRoles(role);
        _auth.SetClaims(new Claim(ClaimTypes.Role, role));
    }

    private IRenderedComponent<SiteHeader> RenderAt(string uri)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(uri);
        return Render<SiteHeader>();
    }
}
