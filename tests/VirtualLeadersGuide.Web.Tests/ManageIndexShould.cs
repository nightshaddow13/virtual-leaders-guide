using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VirtualLeadersGuide.Web.Identity;
using ManageIndexPage = VirtualLeadersGuide.Web.Components.Account.Pages.Manage.Index;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>Named for the page (<c>Manage/Index.razor</c>), not the bare class name (<c>Index</c>).</remarks>
public class ManageIndexShould : BunitContext
{
    [Fact]
    public void RedirectWithAStatusCookie_WhenThePhoneNumberUpdateSucceeds_ForOnValidSubmitAsync()
    {
        var httpContext = new DefaultHttpContext();
        var user = new ApplicationUser { Id = "user-1" };
        UserManager<ApplicationUser> userManager = FakeUserManagerFactory.CreateUserManager();
        userManager.GetUserAsync(httpContext.User).Returns(Task.FromResult<ApplicationUser?>(user));
        userManager.GetUserNameAsync(user).Returns(Task.FromResult<string?>("director@example.org"));
        userManager.GetPhoneNumberAsync(user).Returns(Task.FromResult<string?>(null));
        userManager.SetPhoneNumberAsync(user, Arg.Any<string>()).Returns(Task.FromResult(IdentityResult.Success));
        Services.AddSingleton(userManager);
        Services.AddSingleton(FakeUserManagerFactory.CreateSignInManager(userManager));
        IdentityTestServices.RegisterIdentityRedirectManager(Services);

        IRenderedComponent<ManageIndexPage> cut = Render<ManageIndexPage>(parameters => parameters.AddCascadingValue(httpContext));
        cut.Find("#Input\\.PhoneNumber").Change("555-0100");
        cut.Find("form").Submit();

        Assert.True(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }
}
