using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="InfoPageListShould"/>'s shape - this page gates on
/// <c>ApiEventClient.GetEventAsync</c>, not the Activity collection endpoint alone, and renders the exact
/// same grid for an Admin and an assigned Director (ADR-0069) - no <c>PageState.Admin</c>/<c>Director</c>
/// split, unlike <c>EventEditor</c>. Name-only this slice (P5-7, #93) - no "Placed under" chip column
/// (P5-11, #96) and no row-action column (P5-8/P5-9, #94/#95).
/// </remarks>
public class ActivityListShould : BunitContext
{
    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public ActivityListShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void RedirectToNoAccess_WhenTheSignedInUserHoldsNoRoleClaim_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("user-1");

        Render<ActivityList>(parameters => parameters.Add(component => component.EventId, Guid.NewGuid()));

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("Account/NoAccess", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheDeniedPanel_WhenTheEventReadIsForbidden_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, Guid.NewGuid()));

        Assert.Contains("You don't have access to this Event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheUnavailablePanel_WhenTheEventStoreThrows_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, Guid.NewGuid()));

        Assert.Contains("Something went wrong loading this Event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ListTheEventsActivities_WhenTheSignedInUserIsAnAdmin_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { ActivityResource(eventId, "Canoe Basics") } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("Canoe Basics", cut.Markup, StringComparison.Ordinal);
    }

    /// <remarks>Pins the grilled "no Admin/Director UI split" decision (ADR-0069) - an assigned Director sees the identical list an Admin does, not a narrower view.</remarks>
    [Fact]
    public void ListTheEventsActivities_WhenTheSignedInUserIsAnAssignedDirector_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { ActivityResource(eventId, "Canoe Basics") } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("Canoe Basics", cut.Markup, StringComparison.Ordinal);
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

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
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

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("MY EVENTS", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheEmptyStateCard_WhenTheEventHasNoActivities_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("No activities yet", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("New activity", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowAnInlineError_WhenTheActivityStoreThrows_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Contains("Something went wrong loading Activities", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowOneChipPerPlacementAndTheCounts_WhenAnActivityIsPlacedTwice_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();
        Guid morning = Guid.NewGuid(), afternoon = Guid.NewGuid(), waterfront = Guid.NewGuid();
        var placementHandler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/tabs" => Collection(
                TierResource("tabs", morning, new { eventId, name = "Morning", sortOrder = 0 }),
                TierResource("tabs", afternoon, new { eventId, name = "Afternoon", sortOrder = 1 })),
            "/api/sections" => Collection(TierResource("sections", waterfront, new { eventId, parentTabId = morning, name = "Waterfront", sortOrder = 0 })),
            "/api/placements" => Collection(
                TierResource("placements", Guid.NewGuid(), new { eventId, activityId, tabId = morning, sectionId = waterfront, sortOrder = 0 }),
                TierResource("placements", Guid.NewGuid(), new { eventId, activityId, tabId = afternoon, sortOrder = 0 })),
            _ => Collection()
        });
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { ActivityResource(eventId, "Canoe Basics", activityId) } }),
            placementHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Equal(["Afternoon", "Morning › Waterfront"], cut.FindAll(".al-chip").Select(chip => chip.TextContent));
        Assert.Contains("2 placements", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowNoChips_WhenAnActivityIsNotPlacedAnywhereYet_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { ActivityResource(eventId, "Canoe Basics") } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Empty(cut.FindAll(".al-chip"));
        Assert.Contains("0 placements", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenTheActivitysPage_WhenARowIsClicked_ForOpenActivity()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { ActivityResource(eventId, "Canoe Basics", activityId) } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");
        IRenderedComponent<ActivityList> cut = Render<ActivityList>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        cut.Find("tbody tr").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"dashboard/events/{eventId}/activities/{activityId}", navigation.Uri, StringComparison.Ordinal);
    }

    private void RegisterClients(HttpMessageHandler eventHandler, HttpMessageHandler activityHandler, HttpMessageHandler? placementHandler = null)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateActivityClient(activityHandler));
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

    private static object TierResource(string type, Guid id, object attributes) => new { type, id = id.ToString(), attributes };

    private static HttpResponseMessage Collection(params object[] resources) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { data = resources, meta = new { total = resources.Length } })
    };

    private static object ActivityResource(Guid eventId, string name, Guid? id = null) => new
    {
        type = "activities",
        id = (id ?? Guid.NewGuid()).ToString(),
        attributes = new { eventId, name, description = "content" }
    };
}
