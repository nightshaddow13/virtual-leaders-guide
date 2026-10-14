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
/// resolve-or-create flow (P8-2, #166), plus the edit mode P8-4 (#168) adds: rendering with the <c>Id</c>
/// route parameter set loads the Facility first, and submitting PATCHes instead of POSTing. The create-mode
/// cases mirror <see cref="ActivityEditorShould"/>'s create-path subset; the Admin-only gate runs in
/// <c>OnParametersSetAsync</c> alongside the edit-mode load, like <c>InfoPageEditor</c>.
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
    public void ShowDenied_WhenTheCallerIsNotAnAdmin_ForOnParametersSetAsync()
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
    public void ShowUnavailable_WhenTheFacilityTypeStoreThrows_ForOnParametersSetAsync()
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
        Assert.EndsWith("dashboard/facilities", navigation.Uri, StringComparison.Ordinal);
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
        Assert.EndsWith("dashboard/facilities", navigation.Uri, StringComparison.Ordinal);
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
    public void NavigateToTheFacilityList_WhenSubmittingAValidNewFacility_ForSaveAsync()
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
        Assert.EndsWith("dashboard/facilities", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void PrefillTheNameAndTheResolvedTypeName_WhenEditingAnExistingFacility_ForOnParametersSetAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            EditFacilityHandler(facilityId, facilityTypeId, "Camp Blackhawk", HttpStatusCode.NoContent));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);

        Assert.Equal("Camp Blackhawk", cut.Find("#Name").GetAttribute("value"));
        Assert.Equal("Camp", cut.Find("#TypeName").GetAttribute("value"));
    }

    [Fact]
    public void ShowTheEditCopy_WhenEditingAnExistingFacility_ForOnParametersSetAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            EditFacilityHandler(facilityId, facilityTypeId, "Camp Blackhawk", HttpStatusCode.NoContent));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);

        Assert.Equal("Edit facility", cut.Find("h1").TextContent);
        Assert.Contains("Save changes", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Create facility", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("come next, on the detail page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowTheCreateCopy_WhenCreatingANewFacility_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, EmptyFacilityTypeCollection()),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = Render<FacilityEditor>();

        Assert.Equal("New facility", cut.Find("h1").TextContent);
        Assert.Contains("Create facility", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("come next, on the detail page", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowMissing_WhenTheFacilityDoesNotExist_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, EmptyFacilityTypeCollection()),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(Guid.NewGuid());

        Assert.Contains("This facility no longer exists", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Only Admins can manage Facilities.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDenied_WhenApiRespondsWithForbiddenOnFacilityRead_ForOnParametersSetAsync()
    {
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, EmptyFacilityTypeCollection()),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.Forbidden));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(Guid.NewGuid());

        Assert.Contains("Only Admins can manage Facilities.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SendAPatchAndNeverAPost_WhenSubmittingAnEditToAnExistingType_ForSaveAsync()
    {
        var facilityId = Guid.NewGuid();
        var oldTypeId = Guid.NewGuid();
        var newTypeId = Guid.NewGuid();
        var requests = new List<string>();
        var facilityTypeHandler = new StubHttpMessageHandler(request =>
        {
            requests.Add($"types {request.Method}");
            return JsonResponse(HttpStatusCode.OK, new
            {
                data = new[] { FacilityTypeResource(oldTypeId, "Camp"), FacilityTypeResource(newTypeId, "Retreat Center") }
            });
        });
        string? patchBody = null;
        var facilityHandler = EditFacilityHandler(
            facilityId, oldTypeId, "Camp Blackhawk", HttpStatusCode.NoContent, requests, body => patchBody = body);
        RegisterClients(facilityTypeHandler, facilityHandler);
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);
        cut.Find("#Name").Change("Pinewood");
        cut.Find("#TypeName").Change("Retreat Center");
        ClickSave(cut);

        Assert.Equal(["facilities GET", "facilities PATCH"], requests.Where(r => r.StartsWith("facilities", StringComparison.Ordinal)));
        Assert.DoesNotContain("types POST", requests);
        Assert.NotNull(patchBody);
        Assert.Contains("\"name\":\"Pinewood\"", patchBody, StringComparison.Ordinal);
        Assert.Contains($"\"facilityTypeId\":\"{newTypeId}\"", patchBody, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTheFacilityTypeFirstThenPatch_WhenTheTypedNameMatchesNoExistingType_ForSaveAsync()
    {
        var facilityId = Guid.NewGuid();
        var oldTypeId = Guid.NewGuid();
        var createdTypeId = Guid.NewGuid();
        var requests = new List<string>();
        var facilityTypeHandler = new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                requests.Add("types POST");
                return JsonResponse(HttpStatusCode.Created, new { data = FacilityTypeResource(createdTypeId, "Lodge") });
            }

            return JsonResponse(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(oldTypeId, "Camp") } });
        });
        string? patchBody = null;
        var facilityHandler = EditFacilityHandler(
            facilityId, oldTypeId, "Camp Blackhawk", HttpStatusCode.NoContent, requests, body => patchBody = body);
        RegisterClients(facilityTypeHandler, facilityHandler);
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);
        cut.Find("#TypeName").Change("Lodge");
        ClickSave(cut);

        Assert.Equal(["facilities GET", "types POST", "facilities PATCH"], requests);
        Assert.Contains($"\"facilityTypeId\":\"{createdTypeId}\"", patchBody, StringComparison.Ordinal);
    }

    [Fact]
    public void NotifyAndNavigateToTheFacilityList_WhenSubmittingAValidEdit_ForSaveAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            EditFacilityHandler(facilityId, facilityTypeId, "Camp Blackhawk", HttpStatusCode.NoContent));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);
        cut.Find("#Name").Change("Pinewood");
        ClickSave(cut);

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("dashboard/facilities", navigation.Uri, StringComparison.Ordinal);
        var notifications = Services.GetRequiredService<Radzen.NotificationService>();
        Assert.Contains(notifications.Messages, message => message.Summary == "Changes saved");
    }

    [Fact]
    public void ShowDenied_WhenApiRespondsWithForbiddenOnFacilityUpdate_ForSaveAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            EditFacilityHandler(facilityId, facilityTypeId, "Camp Blackhawk", HttpStatusCode.Forbidden));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);
        cut.Find("#Name").Change("Pinewood");
        ClickSave(cut);

        Assert.Contains("Only Admins can manage Facilities.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowMissing_WhenApiRespondsWithNotFoundOnFacilityUpdate_ForSaveAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            EditFacilityHandler(facilityId, facilityTypeId, "Camp Blackhawk", HttpStatusCode.NotFound));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);
        cut.Find("#Name").Change("Pinewood");
        ClickSave(cut);

        Assert.Contains("This facility no longer exists", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnAlertAndLeaveTheTypeEmpty_WhenTheFacilitysTypeIsNotInTheLoadedList_ForOnParametersSetAsync()
    {
        var facilityId = Guid.NewGuid();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(Guid.NewGuid(), "Camp") } }),
            EditFacilityHandler(facilityId, Guid.NewGuid(), "Camp Blackhawk", HttpStatusCode.NoContent));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);

        Assert.Contains("current Type couldn't be found", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Camp Blackhawk", cut.Find("#Name").GetAttribute("value"));
        Assert.True(string.IsNullOrEmpty(cut.Find("#TypeName").GetAttribute("value")));
    }

    [Fact]
    public void ReloadAndClearThePreviousFacilitysState_WhenTheIdChanges_ForOnParametersSetAsync()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var knownTypeId = Guid.NewGuid();
        var gets = new List<Guid>();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(knownTypeId, "Camp") } }),
            new StubHttpMessageHandler(request =>
            {
                Guid requested = Guid.Parse(request.RequestUri!.AbsolutePath.Split('/')[^1]);
                gets.Add(requested);
                (Guid typeId, string name) = requested == firstId ? (Guid.NewGuid(), "First Camp") : (knownTypeId, "Second Camp");
                return JsonResponse(HttpStatusCode.OK, new { data = FacilityResource(requested, typeId, name) });
            }));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(firstId);
        Assert.Contains("current Type couldn't be found", cut.Markup, StringComparison.Ordinal);

        cut.Render(parameters => parameters.Add(component => component.Id, secondId));

        Assert.Equal([firstId, secondId], gets);
        Assert.Equal("Second Camp", cut.Find("#Name").GetAttribute("value"));
        Assert.Equal("Camp", cut.Find("#TypeName").GetAttribute("value"));
        Assert.DoesNotContain("current Type couldn't be found", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepTheUnsavedEditsAndNotReload_WhenRerenderedWithTheSameId_ForOnParametersSetAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        var requests = new List<string>();
        RegisterClients(
            StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } }),
            EditFacilityHandler(facilityId, facilityTypeId, "Camp Blackhawk", HttpStatusCode.NoContent, requests));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);
        cut.Find("#Name").Change("Half-typed rename");
        cut.Render(parameters => parameters.Add(component => component.Id, facilityId));

        Assert.Equal("Half-typed rename", cut.Find("#Name").GetAttribute("value"));
        Assert.Equal(["facilities GET"], requests);
    }

    [Fact]
    public void BlameTheTypeAndRefreshTheTypeList_WhenApiRejectsTheUpdateOverTheTypePointer_ForSaveAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        int typeListReads = 0;
        var facilityTypeHandler = new StubHttpMessageHandler(_ =>
        {
            typeListReads++;
            return JsonResponse(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } });
        });
        RegisterClients(facilityTypeHandler, UpdateRejectedHandler(facilityId, facilityTypeId, "/data/attributes/facilityTypeId"));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);
        cut.Find("#Name").Change("Pinewood");
        ClickSave(cut);

        Assert.Contains("That Facility Type no longer exists.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(2, typeListReads);
    }

    [Fact]
    public void ShowTheGenericMessageAndLeaveTheTypeListAlone_WhenApiRejectsTheUpdateOverAnotherPointer_ForSaveAsync()
    {
        var facilityId = Guid.NewGuid();
        var facilityTypeId = Guid.NewGuid();
        int typeListReads = 0;
        var facilityTypeHandler = new StubHttpMessageHandler(_ =>
        {
            typeListReads++;
            return JsonResponse(HttpStatusCode.OK, new { data = new[] { FacilityTypeResource(facilityTypeId, "Camp") } });
        });
        RegisterClients(facilityTypeHandler, UpdateRejectedHandler(facilityId, facilityTypeId, "/data/attributes/name"));
        SignInAsAdmin();

        IRenderedComponent<FacilityEditor> cut = RenderForEdit(facilityId);
        cut.Find("#Name").Change("Pinewood");
        ClickSave(cut);

        Assert.Contains("Something went wrong saving this Facility.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Facility Type no longer exists", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(1, typeListReads);
    }

    private static StubHttpMessageHandler UpdateRejectedHandler(Guid facilityId, Guid facilityTypeId, string pointer) =>
        new(request => request.Method == HttpMethod.Patch
            ? JsonResponse(HttpStatusCode.UnprocessableEntity, new { errors = new[] { new { title = "Rejected.", source = new { pointer } } } })
            : JsonResponse(HttpStatusCode.OK, new { data = FacilityResource(facilityId, facilityTypeId, "Camp Blackhawk") }));

    private void SignInAsAdmin()
    {
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");
    }

    private IRenderedComponent<FacilityEditor> RenderForEdit(Guid facilityId) =>
        Render<FacilityEditor>(parameters => parameters.Add(component => component.Id, facilityId));

    /// <summary>Answers the edit page's GET with the given Facility and its PATCH with <paramref name="patchStatus"/>, logging each call as <c>facilities {METHOD}</c>.</summary>
    private static StubHttpMessageHandler EditFacilityHandler(
        Guid facilityId, Guid facilityTypeId, string name, HttpStatusCode patchStatus,
        List<string>? requests = null, Action<string>? onPatchBody = null) =>
        new(request =>
        {
            requests?.Add($"facilities {request.Method}");
            if (request.Method == HttpMethod.Patch)
            {
                onPatchBody?.Invoke(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(patchStatus);
            }

            return JsonResponse(HttpStatusCode.OK, new { data = FacilityResource(facilityId, facilityTypeId, name) });
        });

    private static void ClickCreate(IRenderedComponent<FacilityEditor> cut) => ClickSubmit(cut, "Create facility");

    private static void ClickSave(IRenderedComponent<FacilityEditor> cut) => ClickSubmit(cut, "Save changes");

    private static void ClickSubmit(IRenderedComponent<FacilityEditor> cut, string buttonText)
    {
        IElement submitButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains(buttonText, StringComparison.Ordinal));
        submitButton.Click();
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
