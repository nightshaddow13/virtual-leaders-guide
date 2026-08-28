using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers the <c>PageState</c> transitions <c>OnParametersSetAsync</c>'s own <c>&lt;remarks&gt;</c>
/// documents - no HTTP-level test exists for this page yet, unlike <c>Dashboard.razor</c>.
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

    private void RegisterClients(HttpMessageHandler eventHandler, HttpMessageHandler directorHandler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateEventClient(eventHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateDirectorClient(directorHandler));
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }
}
