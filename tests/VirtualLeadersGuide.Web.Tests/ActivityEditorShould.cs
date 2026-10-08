using System.Net;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>ActivityEditor.razor.cs</c>'s <c>PageState</c> transitions on both its routes - create (P5-6,
/// #87) and edit (P5-8, #94) - mirroring <see cref="InfoPageEditorShould"/>. The Write/Preview pane behavior
/// belongs to <c>MarkdownFieldShould</c> (ADR-0073); the tests here only prove this page binds its model
/// through it.
/// </remarks>
public class ActivityEditorShould : BunitContext
{
    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public ActivityEditorShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<MarkdownRenderer>();
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

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters =>
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

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters =>
            parameters.Add(component => component.EventId, Guid.NewGuid()));

        Assert.Contains("Something went wrong loading this page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowANameValidationMessageAndNeverSubmit_WhenNameIsBlank_ForSaveAsync()
    {
        Guid eventId = Guid.NewGuid();
        bool activityRequestSent = false;
        var activityHandler = new StubHttpMessageHandler(_ =>
        {
            activityRequestSent = true;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        RegisterClients(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }), activityHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        IElement createButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Create activity", StringComparison.Ordinal));
        createButton.Click();

        Assert.Contains("Enter a name.", cut.Markup, StringComparison.Ordinal);
        Assert.False(activityRequestSent);
    }

    [Fact]
    public void NavigateToTheCreatedActivitysEditPage_WhenSubmittingAValidNewActivity_ForCreateAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.Created, new { data = ActivityResource(activityId, eventId, "Canoe Basics") }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        cut.Find("#Name").Change("Canoe Basics");
        IElement createButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Create activity", StringComparison.Ordinal));
        createButton.Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"dashboard/events/{eventId}/activities/{activityId}", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigateToTheActivitiesList_WhenClickingCancel_ForCancel()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        IElement cancelButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Cancel", StringComparison.Ordinal));
        cancelButton.Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"dashboard/events/{eventId}/activities", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenApiRespondsWithForbiddenOnCreate_ForCreateAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        cut.Find("#Name").Change("Canoe Basics");
        IElement createButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Create activity", StringComparison.Ordinal));
        createButton.Click();

        Assert.Contains("You don't have access to this Event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateThePreviewImmediately_WhenTypingWithoutLeavingTheField_ForRender()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        cut.Find("#Description").Input("**jacket**");

        Assert.Contains("<strong>jacket</strong>", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HydrateTheFormFromTheLoadedActivity_WhenEditingAnExistingActivity_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();

        IRenderedComponent<ActivityEditor> cut = RenderEditing(
            eventId, activityId, UpdateRespondingWith(HttpStatusCode.NoContent, ActivityResource(activityId, eventId, "Canoe Basics", "Paddle **strokes**")));

        Assert.Equal("Canoe Basics", cut.Find("#Name").GetAttribute("value"));
        Assert.Contains("<strong>strokes</strong>", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Edit activity", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("CANOE BASICS", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenTheActivityReadIsForbidden_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden));
        SignInAs("director-1", "Director");

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.ActivityId, Guid.NewGuid()));

        Assert.Contains("You don't have access to this Event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenTheLoadedActivityBelongsToADifferentEvent_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();

        IRenderedComponent<ActivityEditor> cut = RenderEditing(
            eventId, activityId, UpdateRespondingWith(HttpStatusCode.NoContent, ActivityResource(activityId, Guid.NewGuid(), "Canoe Basics")));

        Assert.Contains("You don't have access to this Event", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnavailable_WhenTheActivityStoreThrowsOnRead_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")));
        SignInAs("admin-1", "Admin");

        IRenderedComponent<ActivityEditor> cut = Render<ActivityEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.ActivityId, Guid.NewGuid()));

        Assert.Contains("Something went wrong loading this page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChangesAndNavigateToTheList_WhenUpdatingAnExistingActivity_ForUpdateAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();
        HttpRequestMessage? patch = null;
        string? patchBody = null;
        var activityHandler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, ActivityResource(activityId, eventId, "Canoe Basics"));
            }

            patch = request;
            patchBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        IRenderedComponent<ActivityEditor> cut = RenderEditing(eventId, activityId, activityHandler);
        cut.Find("#Name").Change("Canoe Basics II");
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save changes", StringComparison.Ordinal))
            .Click();

        Assert.Equal(HttpMethod.Patch, patch!.Method);
        Assert.Contains("\"name\":\"Canoe Basics II\"", patchBody, StringComparison.Ordinal);
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"dashboard/events/{eventId}/activities", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void DisableTheFieldsetAndShowAMessage_WhenApiRespondsWithForbiddenOnUpdate_ForUpdateAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid activityId = Guid.NewGuid();
        var activityHandler = new StubHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? JsonResponse(HttpStatusCode.OK, ActivityResource(activityId, eventId, "Canoe Basics"))
            : new HttpResponseMessage(HttpStatusCode.Forbidden));

        IRenderedComponent<ActivityEditor> cut = RenderEditing(eventId, activityId, activityHandler, "director-1", "Director");
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save changes", StringComparison.Ordinal))
            .Click();

        Assert.Contains("You no longer have permission to edit this Activity.", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
    }

    private IRenderedComponent<ActivityEditor> RenderEditing(
        Guid eventId, Guid activityId, HttpMessageHandler activityHandler, string user = "admin-1", string role = "Admin")
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            activityHandler);
        SignInAs(user, role);

        return Render<ActivityEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.ActivityId, activityId));
    }

    private void SignInAs(string user, string role)
    {
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized(user);
        auth.SetRoles(role);
    }

    private static StubHttpMessageHandler UpdateRespondingWith(HttpStatusCode updateStatus, object activity) =>
        new(request => request.Method == HttpMethod.Get
            ? JsonResponse(HttpStatusCode.OK, activity)
            : new HttpResponseMessage(updateStatus));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object activity) =>
        new(status) { Content = System.Net.Http.Json.JsonContent.Create(new { data = activity }) };

    private void RegisterClients(HttpMessageHandler eventHandler, HttpMessageHandler activityHandler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateActivityClient(activityHandler));
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

    private static object ActivityResource(Guid id, Guid eventId, string name, string description = "content") => new
    {
        type = "activities",
        id = id.ToString(),
        attributes = new { eventId, name, description }
    };
}
