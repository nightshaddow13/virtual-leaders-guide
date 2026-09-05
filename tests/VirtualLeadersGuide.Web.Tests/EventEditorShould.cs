using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Time;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers the <c>PageState</c> transitions <c>OnParametersSetAsync</c>'s own <c>&lt;remarks&gt;</c>
/// documents - no HTTP-level test exists for this page yet, unlike <c>Dashboard.razor</c>.
/// <c>RenderADisabledRemoveButton_WhenADirectorAlsoHoldsAdmin_ForOnParametersSetAsync</c>'s handler
/// dispatches on query string rather than reusing one fixed body for <c>/api/roleGrants</c> the way most
/// tests in this class do - see <c>ApiDirectorClientShould</c>'s class remarks for why, at the client layer
/// this page's rendering builds on.
/// </remarks>
/// <remarks>
/// Status coverage (P2-20, #115): a not-yet-created Event has nothing to publish - there's no
/// <c>loadedDto</c> at all until Save succeeds, which is why Go live never shows on the New event page.
/// <see cref="VirtualLeadersGuide.Web.Events.EventDto.Status"/> also isn't a form field (Go live is a standalone action, not folded into
/// <c>EditForm</c>), so a <c>/status</c> 422 has no field to land a <c>ValidationMessage</c> on - it routes
/// to the page-level <c>statusErrorMessage</c> instead.
/// </remarks>
public class EventEditorShould : BunitContext
{
    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public EventEditorShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void ShowDenied_WhenCreatingAndTheSignedInUserIsNotAnAdmin_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>();

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

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, Guid.NewGuid()));

        Assert.Contains("Something went wrong loading this Event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBothDateTimeInputs_WhenCreatingAsAnAdmin_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>();

        Assert.Single(cut.FindAll("#StartsAt"));
        Assert.Single(cut.FindAll("#EndsAt"));
    }

    /// <remarks>
    /// Regression coverage for ADR-0043's read-only rendering (the Director view shows time of day through
    /// <c>EventDateRange.FormatWithTime</c>, not the Admin's <c>datetime-local</c> inputs) - <see cref="loadedDto"/>
    /// stays populated for exactly this branch.
    /// </remarks>
    [Fact]
    public void RenderReadOnlyDatesRowWithNoInputs_WhenViewingAsAnAssignedDirector_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.Contains("Dates", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("JUN 12, 2026 2:00 PM", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#StartsAt"));
        Assert.Empty(cut.FindAll("#EndsAt"));
    }

    /// <remarks>
    /// Regression coverage for ADR-0042's 422 handling - <see cref="ApplyFieldErrors"/>'s <c>/endsAt</c>
    /// branch routes the error onto <c>EventFormModel.EndsAtLocal</c>, the same field the Admin just edited.
    /// </remarks>
    [Fact]
    public void ShowEndsAtValidationMessage_WhenApiRespondsWithUnprocessableEntityOnSave_ForSaveAsync()
    {
        Guid eventId = Guid.NewGuid();
        var eventHandler = new StubHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = EventResource(eventId) }) }
            : new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = JsonContent.Create(new
                {
                    errors = new[]
                    {
                        new { title = "Invalid date range.", source = new { pointer = "/data/attributes/endsAt" } }
                    }
                })
            });
        RegisterClients(eventHandler, StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));
        IElement saveButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save changes", StringComparison.Ordinal));
        saveButton.Click();

        Assert.Contains("End must be after the start.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDangerZone_WhenEditingAnExistingEventAsAnAdmin_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.Contains("Danger zone", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Delete event", StringComparison.Ordinal));
    }

    /// <remarks>A not-yet-created Event has nothing to delete - see the Danger zone's own placement remarks.</remarks>
    [Fact]
    public void HideDangerZone_WhenCreatingANewEventAsAnAdmin_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>();

        Assert.DoesNotContain("Danger zone", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HideDangerZone_WhenViewingAsAnAssignedDirector_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.DoesNotContain("Danger zone", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowGoLive_WhenEditingAnExistingDraftEventAsAnAdmin_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId, status: "Draft") }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Go live", StringComparison.Ordinal));
    }

    [Fact]
    public void HideGoLive_WhenCreatingANewEventAsAnAdmin_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>();

        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Go live", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Live")]
    [InlineData("Past")]
    [InlineData("Cancelled")]
    public void HideGoLive_WhenTheEventIsNotDraft_ForOnParametersSetAsync(string status)
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId, status: status) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Go live", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowCancelEvent_WhenTheEventIsLive_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId, status: "Live") }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Cancel event", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Past")]
    [InlineData("Cancelled")]
    public void HideCancelEvent_WhenTheEventIsNotLive_ForOnParametersSetAsync(string status)
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId, status: status) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Cancel event", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowAPageLevelError_WhenApiRejectsGoingLiveWithUnprocessableEntity_ForGoLiveAsync()
    {
        Guid eventId = Guid.NewGuid();
        var eventHandler = new StubHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = EventResource(eventId, status: "Draft") }) }
            : new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = JsonContent.Create(new
                {
                    errors = new[]
                    {
                        new { title = "Invalid status change.", source = new { pointer = "/data/attributes/status" } }
                    }
                })
            });
        RegisterClients(eventHandler, StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));
        IElement goLiveButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Go live", StringComparison.Ordinal));
        goLiveButton.Click();

        Assert.Contains("That change isn't allowed", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".rz-messages-error li"));
    }

    [Fact]
    public void HideGoLiveAndCancelEvent_WhenViewingAsAnAssignedDirector_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId, status: "Live") }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Go live", StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Cancel event", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderAnEnabledRemoveButtonPerDirector_WhenEditingAnExistingEventAsAnAdmin_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        var directorHandler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/roleGrants" => JsonResponse(new { data = new[] { GrantResource("director-1", RoleIds.Director, eventId) } }),
            "/api/users" => JsonResponse(new { data = new[] { UserResource("director-1", "pat@troop12.org", "Pat Riley", hasCredential: true) } }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            directorHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        IElement removeButton = cut.FindAll("button")
            .Single(button => button.GetAttribute("aria-label") == "Remove Pat Riley");
        Assert.False(removeButton.HasAttribute("disabled"));
    }

    [Fact]
    public void RenderADisabledRemoveButton_WhenADirectorAlsoHoldsAdmin_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        var directorHandler = new StubHttpMessageHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            string query = request.RequestUri!.Query;

            if (path == "/api/users")
            {
                return JsonResponse(new { data = new[] { UserResource("admin-director-1", "ash@council.org", "Ash Vance", hasCredential: true) } });
            }

            if (path == "/api/roleGrants" && query.Contains("eventId", StringComparison.Ordinal))
            {
                return JsonResponse(new { data = new[] { GrantResource("admin-director-1", RoleIds.Director, eventId) } });
            }

            if (path == "/api/roleGrants")
            {
                return JsonResponse(new
                {
                    data = new[]
                    {
                        GrantResource("admin-director-1", RoleIds.Director, eventId),
                        GrantResource("admin-director-1", RoleIds.Admin, eventId: null)
                    }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            directorHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        IElement removeButton = cut.FindAll("button")
            .Single(button => button.GetAttribute("aria-label") == "Remove Ash Vance");
        Assert.True(removeButton.HasAttribute("disabled"));
    }

    [Fact]
    public void HideRemoveButtons_WhenViewingAsAnAssignedDirector_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<EventEditor> cut = Render<EventEditor>(parameters => parameters
            .Add(component => component.Id, eventId));

        Assert.DoesNotContain(cut.FindAll("button"),
            button => button.GetAttribute("aria-label")?.StartsWith("Remove ", StringComparison.Ordinal) == true);
    }

    private static object EventResource(Guid id, string status = "Draft") => new
    {
        type = "events",
        id = id.ToString(),
        attributes = new
        {
            name = "Fall Camporee",
            slug = "fall-camporee",
            passcode = "TigerLantern",
            status,
            startsAt = new DateTimeOffset(2026, 6, 12, 14, 0, 0, TimeSpan.Zero),
            endsAt = new DateTimeOffset(2026, 6, 14, 22, 0, 0, TimeSpan.Zero)
        }
    };

    private void RegisterClients(HttpMessageHandler eventHandler, HttpMessageHandler directorHandler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateDirectorClient(directorHandler));
        Services.AddSingleton<BrowserTimeZoneAccessor>();
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    /// <remarks>Mirrors <c>ApiDirectorClientShould</c>'s identically-named helper - kept local rather than shared, matching <see cref="EventResource"/>'s own precedent in this class.</remarks>
    private static object UserResource(string id, string email, string? displayName, bool hasCredential) => new
    {
        type = "users",
        id,
        attributes = new { email, displayName, hasCredential }
    };

    private static object GrantResource(string userId, int roleId, Guid? eventId) => new
    {
        type = "roleGrants",
        id = Guid.NewGuid().ToString(),
        attributes = new { userId, roleId, eventId }
    };

    private static HttpResponseMessage JsonResponse<T>(T body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
