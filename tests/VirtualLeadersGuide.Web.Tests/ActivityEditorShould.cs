using System.Net;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>ActivityEditor.razor.cs</c>'s <c>PageState</c> transitions and the responsive Write/Preview pane
/// markup (both panes always present in the DOM; CSS, not C#, decides visibility - see
/// <c>ActivityEditor.razor.css</c>). Create-only (P5-6, #87) - mirrors the create-path subset of
/// <see cref="InfoPageEditorShould"/>; P5-8 (#94) adds the edit-path coverage once this component gets a
/// second route.
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

    /// <remarks>Repointed by P5-7 (#93) - the create form lands on the new Activities list rather than the Event page, now that one exists.</remarks>
    [Fact]
    public void NavigateToTheActivitiesList_WhenSubmittingAValidNewActivity_ForCreateAsync()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.Created, new { data = ActivityResource(Guid.NewGuid(), eventId, "Canoe Basics") }));
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
        Assert.EndsWith($"dashboard/events/{eventId}/activities", navigation.Uri, StringComparison.Ordinal);
    }

    /// <remarks>P5-7 (#93) - Cancel is repointed to the Activities list the same way a successful create is.</remarks>
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

    /// <remarks>
    /// Regression coverage for the same bug class <c>InfoPageEditorShould.UpdateThePreviewImmediately...</c>
    /// pins (fixed for InfoPage by <c>c866c9a</c>): the textarea's <c>@bind-Value:event="oninput"</c> means
    /// every keystroke updates the Preview pane, not just a blur/tab-away - <c>Input(...)</c> fires the DOM
    /// <c>input</c> event bUnit's binding actually listens for.
    /// </remarks>
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
