using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers the grilled access-guard design (P5-17, #22): this page gates on
/// <c>ApiEventClient.GetEventAsync</c>, not the InfoPage collection endpoint alone, and renders the exact
/// same grid for an Admin and an assigned Director (ADR-0059) - no <c>PageState.Admin</c>/<c>Director</c>
/// split, unlike <c>EventEditor</c>.
/// </remarks>
public class InfoPageListShould : BunitContext
{
    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public InfoPageListShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void RedirectToNoAccess_WhenTheSignedInUserHoldsNoRoleClaim_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("user-1");

        Render<InfoPageList>(parameters => parameters.Add(component => component.EventId, Guid.NewGuid()));

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("Account/NoAccess", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenTheEventReadIsForbidden_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<InfoPageList> cut = Render<InfoPageList>(parameters =>
            parameters.Add(component => component.EventId, Guid.NewGuid()));

        Assert.Contains("You don't have access to this Event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnavailable_WhenTheEventStoreThrows_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageList> cut = Render<InfoPageList>(parameters =>
            parameters.Add(component => component.EventId, Guid.NewGuid()));

        Assert.Contains("Something went wrong loading this Event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowEditAndDeleteIcons_WhenTheSignedInUserIsAnAdmin_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { InfoPageResource(eventId, "Packing List") } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageList> cut = Render<InfoPageList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Edit");
        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Delete");
    }

    /// <remarks>Pins the grilled "no Admin/Director UI split" decision (ADR-0059) - an assigned Director sees the identical Edit/Delete icons an Admin does, not a narrower view.</remarks>
    [Fact]
    public void ShowTheSameEditAndDeleteIcons_WhenViewingAsAnAssignedDirector_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { InfoPageResource(eventId, "Packing List") } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<InfoPageList> cut = Render<InfoPageList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Edit");
        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Delete");
    }

    [Fact]
    public void ShowEventsInTheBreadcrumb_WhenTheSignedInUserIsAnAdmin_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageList> cut = Render<InfoPageList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("EVENTS", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("MY EVENTS", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowMyEventsInTheBreadcrumb_WhenTheSignedInUserIsADirector_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<InfoPageList> cut = Render<InfoPageList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("MY EVENTS", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowEmptyText_WhenNoInfoPagesExist_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageList> cut = Render<InfoPageList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("No Info pages yet.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnInlineError_WhenTheInfoPageStoreIsUnavailable_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageList> cut = Render<InfoPageList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("Something went wrong loading Info pages", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "Logistics › Arrival")]
    [InlineData(false, "Logistics")]
    public void ShowThePlacementAsAChip_WhenAnInfoPageIsPlaced_ForLoadDataAsync(bool onSubTab, string expected)
    {
        Guid eventId = Guid.NewGuid();
        Guid infoPageId = Guid.NewGuid();

        IRenderedComponent<InfoPageList> cut = RenderAsAdmin(eventId, infoPageId, TreeHandler(eventId, infoPageId, onSubTab));

        Assert.Equal([expected], cut.FindAll(".ipl-chip").Select(chip => chip.TextContent));
        Assert.Empty(cut.FindAll(".ipl-unplaced"));
        Assert.Contains("1 placed", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowNotPlaced_WhenAnInfoPageHasNoPlacement_ForLoadDataAsync()
    {
        IRenderedComponent<InfoPageList> cut = RenderAsAdmin(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("Not placed", cut.Find(".ipl-unplaced").TextContent);
        Assert.Empty(cut.FindAll(".ipl-chip"));
        Assert.Contains("0 placed", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnInlineErrorAndNoRows_WhenThePlacementTreeIsUnavailable_ForLoadDataAsync()
    {
        IRenderedComponent<InfoPageList> cut = RenderAsAdmin(
            Guid.NewGuid(), Guid.NewGuid(), StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")));

        Assert.Contains("Something went wrong loading Info pages", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Not placed", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NameThePlacementAndReapedTab_WhenDeletingAPlacedInfoPage_ForDeleteAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid infoPageId = Guid.NewGuid();
        IRenderedComponent<InfoPageList> cut = RenderAsAdmin(eventId, infoPageId, TreeHandler(eventId, infoPageId, onSubTab: false));

        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();

        cut.WaitForAssertion(() => Assert.Contains("removed from Logistics.", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("The Logistics tab disappears from the guide too.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StillOpenTheConfirmationWithADegradedBullet_WhenThePlacementTreeCannotBeRead_ForDeleteAsync()
    {
        int tabReads = 0;
        var placementHandler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == "/api/tabs" && ++tabReads > 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = Array.Empty<object>() }) });
        IRenderedComponent<InfoPageList> cut = RenderAsAdmin(Guid.NewGuid(), Guid.NewGuid(), placementHandler);

        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();

        cut.WaitForAssertion(() => Assert.Contains("Delete Packing List?", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Its placement couldn", cut.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<InfoPageList> RenderAsAdmin(Guid eventId, Guid infoPageId, HttpMessageHandler? placementHandler = null)
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { InfoPageResource(eventId, "Packing List", infoPageId) } }),
            placementHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        return Render<InfoPageList>(parameters => parameters.Add(component => component.EventId, eventId));
    }

    /// <remarks>A Tab "Logistics", optionally with a Sub Tab "Arrival", and the InfoPage placed on the deepest of them.</remarks>
    private static StubHttpMessageHandler TreeHandler(Guid eventId, Guid infoPageId, bool onSubTab)
    {
        Guid tab = Guid.NewGuid();
        Guid? arrival = onSubTab ? Guid.NewGuid() : null;
        return new StubHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                data = request.RequestUri!.AbsolutePath switch
                {
                    "/api/tabs" => new object[] { new { type = "tabs", id = tab.ToString(), attributes = new { eventId, name = "Logistics", sortOrder = 0 } } },
                    "/api/subTabs" when arrival is not null => [new { type = "subTabs", id = arrival.ToString(), attributes = new { eventId, tabId = tab, name = "Arrival", sortOrder = 0 } }],
                    "/api/infoPagePlacements" => [new { type = "infoPagePlacements", id = Guid.NewGuid().ToString(), attributes = new { eventId, infoPageId, tabId = tab, subTabId = arrival } }],
                    _ => []
                }
            })
        });
    }

    private void RegisterClients(HttpMessageHandler eventHandler, HttpMessageHandler infoPageHandler, HttpMessageHandler? placementHandler = null)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateInfoPageClient(infoPageHandler));
        Services.AddSingleton(ApiClientTestFactory.CreatePlacementClient(
            placementHandler ?? StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() })));
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    private static object EventResource(Guid id) => new
    {
        type = "events",
        id = id.ToString(),
        attributes = new
        {
            name = "Fall Camporee",
            slug = "fall-camporee",
            passcode = "TigerLantern",
            status = "Draft",
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null
        }
    };

    private static object InfoPageResource(Guid eventId, string title, Guid? id = null) => new
    {
        type = "infoPages",
        id = (id ?? Guid.NewGuid()).ToString(),
        attributes = new { eventId, title, markdownContent = "content" }
    };
}
