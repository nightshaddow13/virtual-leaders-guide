using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>Dashboard.razor</c>'s own logic that <see cref="DashboardShould"/>/
/// <see cref="DashboardWithRoleClaimShould"/> don't reach - those two drive the page over real HTTP to prove
/// the no-role-redirect/renders-normally behavior at the prerender boundary (see their own remarks for why),
/// not the grid's data loading or the Admin-vs-Director markup difference, neither of which is observable
/// without an interactive render.
/// </remarks>
public class DashboardRenderingShould : BunitContext
{
    /// <remarks>
    /// Radzen components call into JS (e.g. <c>RadzenDataGrid</c>'s <c>Radzen.createDataGrid</c> on first
    /// render) for concerns this test has no stake in - sizing, virtualization. Loose mode returns a
    /// default for any unconfigured call instead of throwing, rather than hand-configuring every Radzen JS
    /// interop call this test doesn't actually exercise.
    /// </remarks>
    public DashboardRenderingShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void RedirectToNoAccess_WhenTheSignedInUserHoldsNoRoleClaim_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("user-1");

        Render<Dashboard>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("Account/NoAccess", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowNewEventButton_WhenTheSignedInUserIsAnAdmin_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = Array.Empty<object>() })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains("+ New event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HideNewEventButton_WhenTheSignedInUserIsADirector_ForOnInitializedAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(StubHttpMessageHandler.RespondingWithJson(
            HttpStatusCode.OK, new { data = Array.Empty<object>() })));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("+ New event", StringComparison.Ordinal));
        Assert.Contains("My events", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnInlineError_WhenTheEventStoreIsUnavailable_ForLoadDataAsync()
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage"))));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<Dashboard> cut = Render<Dashboard>();

        Assert.Contains("Something went wrong loading Events", cut.Markup, StringComparison.Ordinal);
    }
}
