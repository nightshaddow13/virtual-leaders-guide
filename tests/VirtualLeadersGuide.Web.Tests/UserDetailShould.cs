using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>No HTTP-level test exists for this page - it's new with P2-12 (#43).</remarks>
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

    private void RegisterServices(HttpMessageHandler directorHandler)
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
        Services.AddSingleton(ApiClientTestFactory.CreateDirectorClient(directorHandler));

        // DirectorInviteService is only reachable from ResendAsync/RevokeAsync, neither of which any of
        // these tests click through - a working instance is registered purely to satisfy [Inject].
        Services.AddSingleton(DirectorInviteServiceTestFactory.Create(
            FakeUserManagerFactory.CreateUserManager(),
            StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)));
    }

    private static HttpMessageHandler UserAndGrantsHandler(bool hasCredential) =>
        new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            $"/api/users/{UserId}" => JsonResponse(HttpStatusCode.OK, new { data = UserResource(hasCredential) }),
            "/api/roleGrants" => JsonResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

    private static object UserResource(bool hasCredential) => new
    {
        type = "users",
        id = UserId,
        attributes = new { email = "pat@troop12.org", displayName = "Pat Riley", hasCredential, isAdmin = false, isDirector = true }
    };

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
        return response;
    }
}
