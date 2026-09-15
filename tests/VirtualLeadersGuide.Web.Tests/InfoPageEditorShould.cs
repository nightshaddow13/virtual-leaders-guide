using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>InfoPageEditor.razor.cs</c>'s <c>PageState</c> transitions, the grilled decision that Save is
/// actually gated on <c>Title</c>'s validity (via <c>EditForm</c>'s <c>OnValidSubmit</c>, not a header button
/// calling <c>SaveAsync</c> directly like <c>EventEditor</c>), and the responsive Write/Preview pane markup
/// (both panes always present in the DOM; CSS, not C#, decides visibility - see
/// <c>InfoPageEditor.razor.css</c>).
/// </remarks>
public class InfoPageEditorShould : BunitContext
{
    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public InfoPageEditorShould()
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

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters =>
            parameters.Add(component => component.EventId, Guid.NewGuid()));

        Assert.Contains("You don't have access to this Info page", cut.Markup, StringComparison.Ordinal);
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

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters =>
            parameters.Add(component => component.EventId, Guid.NewGuid()));

        Assert.Contains("Something went wrong loading this page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenTheLoadedInfoPageBelongsToADifferentEvent_ForOnParametersSetAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid infoPageId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = InfoPageResource(infoPageId, Guid.NewGuid(), "Packing List") }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InfoPageId, infoPageId));

        Assert.Contains("You don't have access to this Info page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowATitleValidationMessageAndNeverSubmit_WhenTitleIsBlank_ForSaveAsync()
    {
        Guid eventId = Guid.NewGuid();
        bool infoPageRequestSent = false;
        var infoPageHandler = new StubHttpMessageHandler(_ =>
        {
            infoPageRequestSent = true;
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { data = InfoPageResource(Guid.NewGuid(), eventId, "") })
            };
        });
        RegisterClients(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }), infoPageHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        IElement createButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Create page", StringComparison.Ordinal));
        createButton.Click();

        Assert.Contains("Enter a title.", cut.Markup, StringComparison.Ordinal);
        Assert.False(infoPageRequestSent);
    }

    [Fact]
    public void NavigateToTheCreatedInfoPage_WhenSubmittingAValidNewInfoPage_ForCreateAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid createdId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.Created, new { data = InfoPageResource(createdId, eventId, "Packing List") }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        cut.Find("#Title").Change("Packing List");
        IElement createButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Create page", StringComparison.Ordinal));
        createButton.Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"dashboard/events/{eventId}/info-pages/{createdId}", navigation.Uri, StringComparison.Ordinal);
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

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));
        cut.Find("#Title").Change("Packing List");
        IElement createButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Create page", StringComparison.Ordinal));
        createButton.Click();

        Assert.Contains("You don't have access to this Info page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChangesAndNavigateToTheList_WhenUpdatingAnExistingInfoPage_ForUpdateAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid infoPageId = Guid.NewGuid();
        var infoPageHandler = new StubHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = InfoPageResource(infoPageId, eventId, "Packing List") }) }
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        RegisterClients(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }), infoPageHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InfoPageId, infoPageId));
        cut.Find("#Title").Change("Renamed");
        IElement saveButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save changes", StringComparison.Ordinal));
        saveButton.Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"dashboard/events/{eventId}/info-pages", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void DisableTheFieldsetAndShowAMessage_WhenApiRespondsWithForbiddenOnUpdate_ForUpdateAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid infoPageId = Guid.NewGuid();
        var infoPageHandler = new StubHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = InfoPageResource(infoPageId, eventId, "Packing List") }) }
            : new HttpResponseMessage(HttpStatusCode.Forbidden));
        RegisterClients(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }), infoPageHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InfoPageId, infoPageId));
        IElement saveButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save changes", StringComparison.Ordinal));
        saveButton.Click();

        Assert.Contains("You no longer have permission to edit this Info page.", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
    }

    [Fact]
    public void NavigateToTheList_WhenDeleteIsConfirmed_ForDeleteAsync()
    {
        Guid eventId = Guid.NewGuid();
        Guid infoPageId = Guid.NewGuid();
        var infoPageHandler = new StubHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data = InfoPageResource(infoPageId, eventId, "Packing List") }) }
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        RegisterClients(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }), infoPageHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InfoPageId, infoPageId));
        cut.FindAll("button").Single(button => button.TextContent.Contains("Delete info page", StringComparison.Ordinal)).Click();
        cut.FindAll("button").Single(button => button.TextContent == "Delete").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"dashboard/events/{eventId}/info-pages", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBothWriteAndPreviewPanes_RegardlessOfActivePane_ForRender()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Single(cut.FindAll(".ip-pane-write"));
        Assert.Single(cut.FindAll(".ip-pane-preview"));
    }

    [Fact]
    public void SwitchTheActivePaneAttribute_WhenPreviewIsSelected_ForRender()
    {
        Guid eventId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters =>
            parameters.Add(component => component.EventId, eventId));

        Assert.Equal("Write", cut.Find(".ip-panes").GetAttribute("data-active-pane"));

        cut.FindAll("button[role=radio]")
            .Single(item => item.TextContent.Contains("Preview", StringComparison.Ordinal))
            .Click();

        Assert.Equal("Preview", cut.Find(".ip-panes").GetAttribute("data-active-pane"));
    }

    [Fact]
    public void RenderTheSanitizedPreview_WhenMarkdownContentIsSet_ForRender()
    {
        Guid eventId = Guid.NewGuid();
        Guid infoPageId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = EventResource(eventId) }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = InfoPageResource(infoPageId, eventId, "About", "**bold**") }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<InfoPageEditor> cut = Render<InfoPageEditor>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.InfoPageId, infoPageId));

        Assert.Contains("<strong>bold</strong>", cut.Markup, StringComparison.Ordinal);
    }

    private void RegisterClients(HttpMessageHandler eventHandler, HttpMessageHandler infoPageHandler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateInfoPageClient(infoPageHandler));
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

    private static object InfoPageResource(Guid id, Guid eventId, string title, string markdownContent = "content") => new
    {
        type = "infoPages",
        id = id.ToString(),
        attributes = new { eventId, title, markdownContent }
    };
}
