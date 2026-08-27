using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>No HTTP-level test exists for this page - it's new with P2-12 (#43).</remarks>
public class UsersShould : BunitContext
{
    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public UsersShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void ShowTheInviteButton_WhenTheSignedInUserIsAnAdmin_ForOnInitializedAsync()
    {
        RegisterDirectorClient(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Users> cut = Render<Users>();

        Assert.Contains("+ Invite director", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenTheSignedInUserIsNotAnAdmin_ForOnInitializedAsync()
    {
        RegisterDirectorClient(StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<Users> cut = Render<Users>();

        Assert.Contains("Only Admins can manage Users", cut.Markup, StringComparison.Ordinal);
    }

    private void RegisterDirectorClient(HttpMessageHandler directorHandler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateDirectorClient(directorHandler));
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }
}
