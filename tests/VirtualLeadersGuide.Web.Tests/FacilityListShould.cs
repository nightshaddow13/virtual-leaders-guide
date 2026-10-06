using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers the grilled access-guard design (P8-3, #167): this page is Admin-only despite
/// <c>FacilityAccessPolicy.CanRead</c> being open to any signed-in Admin or Director (ADR-0070's amendment) -
/// a Director sees the Denied panel, not a narrower grid. Mirrors <see cref="ActivityListShould"/>'s shape
/// otherwise.
/// </remarks>
public class FacilityListShould : BunitContext
{
    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public FacilityListShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void ShowTheDeniedPanel_WhenTheSignedInUserIsADirector_ForOnInitializedAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<FacilityList> cut = Render<FacilityList>();

        Assert.Contains("Only Admins can manage Facilities.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheDeniedPanel_WhenTheFacilityTypeReadIsForbidden_ForOnInitializedAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityList> cut = Render<FacilityList>();

        Assert.Contains("Only Admins can manage Facilities.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheUnavailablePanel_WhenTheFacilityTypeStoreThrows_ForOnInitializedAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityList> cut = Render<FacilityList>();

        Assert.Contains("Something went wrong loading this page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ListEachFacilitysNameAndResolvedType_WhenTheSignedInUserIsAnAdmin_ForLoadDataAsync()
    {
        var facilityTypeId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityResource(facilityTypeId, "Camp Blackhawk") } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityList> cut = Render<FacilityList>();

        Assert.Contains("Camp Blackhawk", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Camp", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheDeniedPanel_WhenTheFacilityCollectionReadIsForbidden_ForLoadDataAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityList> cut = Render<FacilityList>();

        Assert.Contains("Only Admins can manage Facilities.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheEmptyStateCard_WhenThereAreNoFacilities_ForLoadDataAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityList> cut = Render<FacilityList>();

        Assert.Contains("No facilities yet", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Add your first facility", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowAnInlineError_WhenTheFacilityStoreThrows_ForLoadDataAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            StubHttpMessageHandler.ThrowingOn(() => new HttpRequestException("simulated Api outage")));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityList> cut = Render<FacilityList>();

        Assert.Contains("Something went wrong loading Facilities", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAFallback_WhenAFacilitysTypeIdMatchesNoLoadedType_ForLoadDataAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityResource(Guid.NewGuid(), "Pinewood Retreat Center") } }));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<FacilityList> cut = Render<FacilityList>();

        Assert.Contains("Pinewood Retreat Center", cut.Markup, StringComparison.Ordinal);
        Assert.Contains('—'.ToString(), cut.Markup, StringComparison.Ordinal);
    }

    private void RegisterClients(HttpMessageHandler facilityTypeHandler, HttpMessageHandler facilityHandler)
    {
        Services.AddSingleton(ApiClientTestFactory.CreateFacilityTypeClient(facilityTypeHandler));
        Services.AddSingleton(ApiClientTestFactory.CreateFacilityClient(facilityHandler));
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
    }

    private static object FacilityTypeResource(Guid id, string name) => new
    {
        type = "facilityTypes",
        id = id.ToString(),
        attributes = new { name }
    };

    private static object FacilityResource(Guid facilityTypeId, string name) => new
    {
        type = "facilities",
        id = Guid.NewGuid().ToString(),
        attributes = new { name, facilityTypeId }
    };
}
