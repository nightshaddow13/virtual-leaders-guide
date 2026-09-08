using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// No HTTP-level test exists for this page - it's new with P2-12 (#43). <c>DirectorInviteService</c> is
/// only reachable from <c>ResendAsync</c>/<c>RevokeAsync</c>/<c>DeleteAsync</c>, none of which any test
/// below clicks through - <see cref="RegisterServices"/> registers a working instance purely to satisfy
/// <c>[Inject]</c>. The Danger zone tests below only assert the button's enabled/disabled state (ADR-0045's
/// guard rules), the same way <c>EventEditorShould.RenderADisabledRemoveButton_WhenADirectorAlsoHoldsAdmin_ForOnParametersSetAsync</c>
/// covers its own admin-guard button.
/// </remarks>
public class UserDetailShould : BunitContext
{
    private const string UserId = "user-1";

    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public UserDetailShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void ShowDenied_WhenTheSignedInUserIsNotAnAdmin_ForOnParametersSetAsync()
    {
        RegisterServices(StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("director-1");
        auth.SetRoles("Director");

        IRenderedComponent<UserDetail> cut = Render<UserDetail>(parameters => parameters.Add(c => c.Id, UserId));

        Assert.Contains("Only Admins can manage Users", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowResendAndRevoke_WhenTheUserHasNoCredentialYet_ForOnParametersSetAsync()
    {
        RegisterServices(UserAndGrantsHandler(hasCredential: false));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<UserDetail> cut = Render<UserDetail>(parameters => parameters.Add(c => c.Id, UserId));

        Assert.Contains("Resend email", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Revoke invite", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HideResendAndRevoke_WhenTheUserAlreadyHasACredential_ForOnParametersSetAsync()
    {
        RegisterServices(UserAndGrantsHandler(hasCredential: true));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<UserDetail> cut = Render<UserDetail>(parameters => parameters.Add(c => c.Id, UserId));

        Assert.DoesNotContain("Resend email", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HideTheDangerZone_WhenTheUserHasNoCredentialYet_ForOnParametersSetAsync()
    {
        RegisterServices(UserAndGrantsHandler(hasCredential: false));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");

        IRenderedComponent<UserDetail> cut = Render<UserDetail>(parameters => parameters.Add(c => c.Id, UserId));

        Assert.DoesNotContain("Danger zone", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAnEnabledDeleteButton_WhenTheUserIsActivatedAndNotAdminOrSelf_ForOnParametersSetAsync()
    {
        RegisterServices(UserAndGrantsHandler(hasCredential: true, isAdmin: false));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "admin-1"));

        IRenderedComponent<UserDetail> cut = Render<UserDetail>(parameters => parameters.Add(c => c.Id, UserId));

        IElement deleteButton = FindDeleteButton(cut);
        Assert.False(deleteButton.HasAttribute("disabled"));
    }

    [Fact]
    public void ShowADisabledDeleteButton_WhenTheTargetHoldsTheAdminRole_ForOnParametersSetAsync()
    {
        RegisterServices(UserAndGrantsHandler(hasCredential: true, isAdmin: true));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized("admin-1");
        auth.SetRoles("Admin");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, "admin-1"));

        IRenderedComponent<UserDetail> cut = Render<UserDetail>(parameters => parameters.Add(c => c.Id, UserId));

        IElement deleteButton = FindDeleteButton(cut);
        Assert.True(deleteButton.HasAttribute("disabled"));
    }

    /// <remarks>The signed-in Admin viewing their own detail page - ADR-0045's self-target guard.</remarks>
    [Fact]
    public void ShowADisabledDeleteButton_WhenTheTargetIsTheSignedInCaller_ForOnParametersSetAsync()
    {
        RegisterServices(UserAndGrantsHandler(hasCredential: true, isAdmin: false));
        Bunit.TestDoubles.BunitAuthorizationContext auth = this.AddAuthorization();
        auth.SetAuthorized(UserId);
        auth.SetRoles("Admin");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, UserId));

        IRenderedComponent<UserDetail> cut = Render<UserDetail>(parameters => parameters.Add(c => c.Id, UserId));

        IElement deleteButton = FindDeleteButton(cut);
        Assert.True(deleteButton.HasAttribute("disabled"));
    }

    private static IElement FindDeleteButton(IRenderedComponent<UserDetail> cut) =>
        cut.FindAll("button").Single(button => button.TextContent.Contains("Delete user", StringComparison.Ordinal));

    private void RegisterServices(HttpMessageHandler directorHandler)
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
        Services.AddSingleton(ApiClientTestFactory.CreateDirectorClient(directorHandler));
        Services.AddSingleton(DirectorInviteServiceTestFactory.Create(
            FakeUserManagerFactory.CreateUserManager(),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
    }

    /// <remarks>
    /// <c>ApiDirectorClient.GetUserAsync</c> derives <c>IsAdmin</c> from the joined <c>/api/roleGrants</c>
    /// response, not from <c>/api/users</c>' own attributes - so <paramref name="isAdmin"/> shapes the
    /// roleGrants response, not <see cref="UserResource"/>'s attributes.
    /// </remarks>
    private static HttpMessageHandler UserAndGrantsHandler(bool hasCredential, bool isAdmin = false) =>
        new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            $"/api/users/{UserId}" => JsonResponse(HttpStatusCode.OK, new { data = UserResource(hasCredential) }),
            "/api/roleGrants" => JsonResponse(HttpStatusCode.OK, new { data = GrantsResource(isAdmin) }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

    private static object UserResource(bool hasCredential) => new
    {
        type = "users",
        id = UserId,
        attributes = new { email = "pat@troop12.org", displayName = "Pat Riley", hasCredential, isAdmin = false, isDirector = true }
    };

    private static object[] GrantsResource(bool isAdmin) => isAdmin ? AdminRoleGrantResource.ForUser(UserId) : [];

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
        return response;
    }
}
