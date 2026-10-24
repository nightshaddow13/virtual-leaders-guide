using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="InfoPageListShould"/>'s shape - this page gates on
/// <c>ApiEventClient.GetEventAsync</c>, not the Activity collection endpoint alone, and renders the exact
/// same grid for an Admin and an assigned Director (ADR-0069) - no <c>PageState.Admin</c>/<c>Director</c>
/// split, unlike <c>EventEditor</c>. The row actions are Edit (P5-8, #94) and Delete (P5-9, #95).
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
        Assert.EndsWith($"dashboard/events/{eventId}/activities/{activityId}/edit", navigation.Uri, StringComparison.Ordinal);
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

    // --- Delete (P5-9, #95) -----------------------------------------------------------------------------

    [Theory]
    [InlineData("admin-1", "Admin")]
    [InlineData("director-1", "Director")]
    public void ShowADeleteIcon_WhenTheSignedInUserIsAnAdminOrAnAssignedDirector_ForLoadDataAsync(string user, string role)
    {
        IRenderedComponent<ActivityList> cut = RenderWithOneActivity(user, role, Guid.NewGuid());

        Assert.Contains(cut.FindAll("button"), button => button.GetAttribute("aria-label") == "Delete");
    }

    [Fact]
    public void ConfirmWithThePlacementsAndTiersThenDeleteAndDropTheRow_WhenTheUserConfirms_ForDeleteAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();
        var deleted = new List<string>();
        IRenderedComponent<ActivityList> cut = RenderForDeletion(eventId, activityId, deleted);

        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();

        cut.WaitForAssertion(() => Assert.Contains("Delete Canoe Basics?", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Its 2 placements are removed.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("The Afternoon tab and Morning tab disappear from the guide too.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(deleted);

        Services.GetRequiredService<DialogService>().Close(true);

        cut.WaitForAssertion(() => Assert.Equal([activityId.ToString()], deleted));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Canoe Basics", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void SendNothingAndKeepTheRow_WhenTheUserCancelsTheConfirmation_ForDeleteAsync()
    {
        var deleted = new List<string>();
        IRenderedComponent<ActivityList> cut = RenderForDeletion(Guid.NewGuid(), Guid.NewGuid(), deleted);
        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();
        cut.WaitForAssertion(() => Assert.Contains("Delete Canoe Basics?", cut.Markup, StringComparison.Ordinal));

        Services.GetRequiredService<DialogService>().Close(false);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Delete Canoe Basics?", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(deleted);
        Assert.Contains("Canoe Basics", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnErrorAndKeepTheRow_WhenTheDeleteIsForbidden_ForDeleteAsync()
    {
        var deleted = new List<string>();
        IRenderedComponent<ActivityList> cut = RenderForDeletion(Guid.NewGuid(), Guid.NewGuid(), deleted, HttpStatusCode.Forbidden);
        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();
        cut.WaitForAssertion(() => Assert.Contains("Delete Canoe Basics?", cut.Markup, StringComparison.Ordinal));

        Services.GetRequiredService<DialogService>().Close(true);

        cut.WaitForAssertion(() => Assert.Contains("You don't have permission to delete this Activity.", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Canoe Basics", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnError_WhenTheActivityStoreFailsTheDelete_ForDeleteAsync()
    {
        var deleted = new List<string>();
        IRenderedComponent<ActivityList> cut = RenderForDeletion(Guid.NewGuid(), Guid.NewGuid(), deleted, HttpStatusCode.InternalServerError);
        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();
        cut.WaitForAssertion(() => Assert.Contains("Delete Canoe Basics?", cut.Markup, StringComparison.Ordinal));

        Services.GetRequiredService<DialogService>().Close(true);

        cut.WaitForAssertion(() => Assert.Contains("Something went wrong deleting this Activity. Try again.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void TreatItAsDeleted_WhenApiSaysTheActivityIsAlreadyGone_ForDeleteAsync()
    {
        var deleted = new List<string>();
        IRenderedComponent<ActivityList> cut = RenderForDeletion(Guid.NewGuid(), Guid.NewGuid(), deleted, HttpStatusCode.NotFound);
        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();
        cut.WaitForAssertion(() => Assert.Contains("Delete Canoe Basics?", cut.Markup, StringComparison.Ordinal));

        Services.GetRequiredService<DialogService>().Close(true);

        cut.WaitForAssertion(() => Assert.Single(deleted));
        Assert.DoesNotContain("went wrong", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("permission", cut.Markup, StringComparison.Ordinal);
    }

    /// <remarks>ADR-0045: the Placement tree is advisory to the dialog, so a failed re-read still opens it, with the Placement and Tier bullets degraded.</remarks>
    [Fact]
    public void StillOpenTheConfirmationWithDegradedBullets_WhenThePlacementTreeCannotBeRead_ForDeleteAsync()
    {
        var deleted = new List<string>();
        IRenderedComponent<ActivityList> cut = RenderForDeletion(Guid.NewGuid(), Guid.NewGuid(), deleted, treeFailsAfterFirstLoad: true);

        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();

        cut.WaitForAssertion(() => Assert.Contains("Delete Canoe Basics?", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Its placements couldn't be loaded", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("be undone.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NotOpenTheActivitysPage_WhenTheRowsDeleteIconIsClicked_ForDeleteAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();
        IRenderedComponent<ActivityList> cut = RenderForDeletion(eventId, activityId, []);

        cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == "Delete").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.DoesNotContain(activityId.ToString(), navigation.Uri, StringComparison.Ordinal);
    }

    /// <remarks>
    /// One Activity ("Canoe Basics") placed at the Tabs Morning and Afternoon, nothing else under either, so a
    /// deletion reaps both. <paramref name="treeFailsAfterFirstLoad"/> lets the grid's own load succeed and then
    /// fails the dialog's re-read of the Placement tree.
    /// </remarks>
    private IRenderedComponent<ActivityList> RenderForDeletion(
        Guid eventId, Guid activityId, List<string> deleted, HttpStatusCode deleteStatus = HttpStatusCode.NoContent,
        bool treeFailsAfterFirstLoad = false)
    {
        Guid morning = Guid.NewGuid(), afternoon = Guid.NewGuid();
        int placementReads = 0;
        var placementHandler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/placements" && ++placementReads > 1 && treeFailsAfterFirstLoad)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return request.RequestUri.AbsolutePath switch
            {
                "/api/tabs" => Collection(
                    TierResource("tabs", morning, new { eventId, name = "Morning", sortOrder = 0 }),
                    TierResource("tabs", afternoon, new { eventId, name = "Afternoon", sortOrder = 1 })),
                "/api/placements" => Collection(
                    TierResource("placements", Guid.NewGuid(), new { eventId, activityId, tabId = morning, sortOrder = 0 }),
                    TierResource("placements", Guid.NewGuid(), new { eventId, activityId, tabId = afternoon, sortOrder = 0 })),
                _ => Collection()
            };
        });
        var activityHandler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                deleted.Add(request.RequestUri!.AbsolutePath[(request.RequestUri.AbsolutePath.LastIndexOf('/') + 1)..]);
                return new HttpResponseMessage(deleteStatus);
            }

            return deleted.Count > 0 && deleteStatus == HttpStatusCode.NoContent
                ? Collection()
                : Collection(ActivityResource(eventId, "Canoe Basics", activityId));
        });
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            activityHandler, placementHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        return Render<ActivityList>(parameters => parameters.Add(component => component.EventId, eventId));
    }

    private void RegisterClients(HttpMessageHandler eventHandler, HttpMessageHandler activityHandler, HttpMessageHandler? placementHandler = null)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateActivityClient(activityHandler));
        Services.AddSingleton(ApiClientTestFactory.CreatePlacementClient(
            placementHandler ?? StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() })));
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
