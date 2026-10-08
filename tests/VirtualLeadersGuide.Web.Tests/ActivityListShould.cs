using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="InfoPageListShould"/>'s shape - this page gates on
/// <c>ApiEventClient.GetEventAsync</c>, not the Activity collection endpoint alone, and renders the exact
/// same grid for an Admin and an assigned Director (ADR-0069) - no <c>PageState.Admin</c>/<c>Director</c>
/// split, unlike <c>EventEditor</c>. No "Placed under" chip column yet (P5-11, #96); the only row action is
/// Edit (P5-8, #94) - Delete waits on P5-9 (#95).
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
    public void ShowAnEditIcon_WhenTheSignedInUserIsAnAdmin_ForLoadDataAsync()
    {
        IRenderedComponent<ActivityList> cut = RenderWithOneActivity("admin-1", "Admin", Guid.NewGuid());

        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Edit");
    }

    [Fact]
    public void ShowTheSameEditIcon_WhenTheSignedInUserIsAnAssignedDirector_ForLoadDataAsync()
    {
        IRenderedComponent<ActivityList> cut = RenderWithOneActivity("director-1", "Director", Guid.NewGuid());

        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Edit");
    }

    [Fact]
    public void NavigateToTheActivityEditor_WhenTheRowsEditIconIsClicked_ForLoadDataAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();
        IRenderedComponent<ActivityList> cut = RenderWithOneActivity("admin-1", "Admin", eventId, activityId);

        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Edit").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"dashboard/events/{eventId}/activities/{activityId}", navigation.Uri, StringComparison.Ordinal);
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

    private void RegisterClients(HttpMessageHandler eventHandler, HttpMessageHandler activityHandler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateActivityClient(activityHandler));
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    private IRenderedComponent<ActivityList> RenderWithOneActivity(string user, string role, Guid eventId, Guid? activityId = null)
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { ActivityResource(eventId, "Canoe Basics", activityId) } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized(user);
        auth.SetRoles(role);

        return Render<ActivityList>(parameters => parameters.Add(component => component.EventId, eventId));
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

    private static object ActivityResource(Guid eventId, string name, Guid? id = null) => new
    {
        type = "activities",
        id = (id ?? Guid.NewGuid()).ToString(),
        attributes = new { eventId, name, description = "content" }
    };
}
