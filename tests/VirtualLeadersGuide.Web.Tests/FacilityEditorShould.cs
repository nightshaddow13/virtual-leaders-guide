using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>FacilityEditor.razor.cs</c>'s <c>PageState</c> transitions and the Facility Type
/// resolve-or-create flow (P8-2, #166). Create-only - mirrors the create-path subset of
/// <see cref="ActivityEditorShould"/>, with no <c>EventId</c> route parameter to resolve first (the
/// Admin-only gate runs in <c>OnInitializedAsync</c> instead of <c>OnParametersSetAsync</c>).
/// <c>RadzenAutoComplete</c> needs <c>JSInterop</c> set to <see cref="JSRuntimeMode.Loose"/> for its popup
/// JS interop call, same as every other Radzen-heavy component test in this project (see
/// <see cref="DashboardRenderingShould"/>'s constructor remarks).
/// </remarks>
public class FacilityEditorShould : BunitContext
{
    public FacilityEditorShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShowDenied_WhenTheCallerIsNotAnAdmin_ForOnInitializedAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, EmptyFacilityTypeCollection()),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();

        Assert.Contains("Only Admins can manage Facilities.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowUnavailable_WhenTheFacilityTypeStoreThrows_ForOnInitializedAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();

        Assert.Contains("Something went wrong loading this page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowANameValidationMessageAndNeverSubmit_WhenNameIsBlank_ForSaveAsync()
    {
        bool facilityRequestSent = false;
        var facilityHandler = new StubHttpMessageHandler(_ =>
        {
            facilityRequestSent = true;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        RegisterClients(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, EmptyFacilityTypeCollection()), facilityHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();
        cut.Find("#TypeName").Change("Camp");
        ClickCreate(cut);

        Assert.Contains("Enter a name.", cut.Markup, StringComparison.Ordinal);
        Assert.False(facilityRequestSent);
    }

    [Fact]
    public void ShowATypeValidationMessageAndNeverSubmit_WhenTypeNameIsBlank_ForSaveAsync()
    {
        bool facilityRequestSent = false;
        var facilityHandler = new StubHttpMessageHandler(_ =>
        {
            facilityRequestSent = true;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        RegisterClients(StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, EmptyFacilityTypeCollection()), facilityHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();
        cut.Find("#Name").Change("Camp Blackhawk");
        ClickCreate(cut);

        Assert.Contains("Enter or pick a type.", cut.Markup, StringComparison.Ordinal);
        Assert.False(facilityRequestSent);
    }

    [Fact]
    public void SendOnlyTheFacilityCreateCall_WhenTheTypedNameMatchesAnExistingType_ForSaveAsync()
    {
        var facilityTypeId = Guid.NewGuid();
        bool facilityTypeWriteSent = false;
        var facilityTypeHandler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                facilityTypeWriteSent = true;
            }

            return JsonResponse(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } });
        });
        RegisterClients(
            facilityTypeHandler,
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.Created, new { data = FacilityResource(Guid.NewGuid(), facilityTypeId, "Camp Blackhawk") }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();
        cut.Find("#Name").Change("Camp Blackhawk");
        cut.Find("#TypeName").Change("Camp");
        ClickCreate(cut);

        Assert.False(facilityTypeWriteSent);
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("dashboard", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTheFacilityTypeFirst_WhenTheTypedNameMatchesNoExistingType_ForSaveAsync()
    {
        var createdTypeId = Guid.NewGuid();
        var requestsInOrder = new List<string>();
        var facilityTypeHandler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                requestsInOrder.Add("facilityTypes");
                return JsonResponse(HttpStatusCode.Created, new { data = FacilityTypeResource(createdTypeId, "Retreat Center") });
            }

            return JsonResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() });
        });
        var facilityHandler = new StubHttpMessageHandler(_ =>
        {
            requestsInOrder.Add("facilities");
            return JsonResponse(HttpStatusCode.Created, new { data = FacilityResource(Guid.NewGuid(), createdTypeId, "Pinewood") });
        });
        RegisterClients(facilityTypeHandler, facilityHandler);
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();
        cut.Find("#Name").Change("Pinewood");
        cut.Find("#TypeName").Change("Retreat Center");
        ClickCreate(cut);

        Assert.Equal(["facilityTypes", "facilities"], requestsInOrder);
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("dashboard", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenApiRespondsWithForbiddenOnFacilityCreate_ForSaveAsync()
    {
        var facilityTypeId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();
        cut.Find("#Name").Change("Camp Blackhawk");
        cut.Find("#TypeName").Change("Camp");
        ClickCreate(cut);

        Assert.Contains("Only Admins can manage Facilities.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigateToTheDashboard_WhenSubmittingAValidNewFacility_ForSaveAsync()
    {
        var facilityTypeId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.Created, new { data = FacilityResource(Guid.NewGuid(), facilityTypeId, "Camp Blackhawk") }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();
        cut.Find("#Name").Change("Camp Blackhawk");
        cut.Find("#TypeName").Change("Camp");
        ClickCreate(cut);

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("dashboard", navigation.Uri, StringComparison.Ordinal);
    }

    private static void ClickCreate(IRenderedComponent<FacilityEditor> cut)
    {
        IElement createButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Create facility", StringComparison.Ordinal));
        createButton.Click();
    }

    private void RegisterClients(HttpMessageHandler facilityTypeHandler, HttpMessageHandler facilityHandler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateFacilityTypeClient(facilityTypeHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateFacilityClient(facilityHandler));
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    private static object EmptyFacilityTypeCollection() => new { data = Array.Empty<object>() };

    private static object FacilityTypeResource(Guid id, string name) => new
    {
        type = "facilityTypes",
        id = id.ToString(),
        attributes = new { name }
    };

    private static object FacilityResource(Guid id, Guid facilityTypeId, string name) => new
    {
        type = "facilities",
        id = id.ToString(),
        attributes = new { name, facilityTypeId }
    };

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T body) =>
        new(statusCode) { Content = JsonContent.Create(body) };
}
