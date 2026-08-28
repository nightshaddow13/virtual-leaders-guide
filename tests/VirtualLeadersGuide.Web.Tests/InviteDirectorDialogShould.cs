using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VirtualLeadersGuide.Web.Components.Pages;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

public class InviteDirectorDialogShould : BunitContext
{
    private const string ExistingEmail = "pat@troop12.org";
    private const string NewEmail = "new@troop12.org";
    private const string ExistingUserId = "user-existing";

    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public InviteDirectorDialogShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void MoveToExistingUserStep_WhenTheEmailAlreadyBelongsToAUser_ForContinueAsync()
    {
        UserManager<ApplicationUser> userManager = FakeUserManagerFactory.CreateUserManager();
        userManager.FindByEmailAsync(ExistingEmail).Returns(Task.FromResult<ApplicationUser?>(
            new ApplicationUser { Id = ExistingUserId, Email = ExistingEmail, UserName = ExistingEmail }));
        RegisterServices(userManager, UserAndGrantsHandler());

        IRenderedComponent<InviteDirectorDialog> cut = Render<InviteDirectorDialog>();
        cut.Find("#Email").Change(ExistingEmail);
        cut.Find("form").Submit();

        Assert.Contains("already exists", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveToNewEmailStep_WhenTheEmailBelongsToNoOne_ForContinueAsync()
    {
        UserManager<ApplicationUser> userManager = FakeUserManagerFactory.CreateUserManager();
        userManager.FindByEmailAsync(NewEmail).Returns(Task.FromResult<ApplicationUser?>(null));
        RegisterServices(userManager, StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound));

        IRenderedComponent<InviteDirectorDialog> cut = Render<InviteDirectorDialog>();
        cut.Find("#Email").Change(NewEmail);
        cut.Find("form").Submit();

        Assert.Contains("Submitting creates their user", cut.Markup, StringComparison.Ordinal);
    }

    private void RegisterServices(UserManager<ApplicationUser> userManager, HttpMessageHandler directorHandler)
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
        Services.AddSingleton(DirectorInviteServiceTestFactory.Create(userManager, directorHandler));
    }

    private static HttpMessageHandler UserAndGrantsHandler() =>
        new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            $"/api/users/{ExistingUserId}" => JsonResponse(HttpStatusCode.OK, new
            {
                data = new
                {
                    type = "users",
                    id = ExistingUserId,
                    attributes = new { email = ExistingEmail, displayName = "Pat Riley", hasCredential = true, isAdmin = false, isDirector = true }
                }
            }),
            "/api/roleGrants" => JsonResponse(HttpStatusCode.OK, new { data = Array.Empty<object>() }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");
        return response;
    }
}
