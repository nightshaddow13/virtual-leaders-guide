using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VirtualLeadersGuide.Web.Components.Account.Pages.Manage;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

public class ChangePasswordShould : BunitContext
{
    [Fact]
    public void RedirectWithAStatusCookie_WhenTheChangeSucceeds_ForOnValidSubmitAsync()
    {
        var httpContext = new DefaultHttpContext();
        var user = new ApplicationUser { Id = "user-1" };
        (UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =
            RegisterServices(httpContext, user);
        userManager.ChangePasswordAsync(user, Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        IRenderedComponent<ChangePassword> cut = Render<ChangePassword>(parameters => parameters
            .AddCascadingValue(httpContext));
        cut.Find("#Input\\.OldPassword").Change("old-password");
        cut.Find("#Input\\.NewPassword").Change("NewP@ssw0rd1");
        cut.Find("#Input\\.ConfirmPassword").Change("NewP@ssw0rd1");
        cut.Find("form").Submit();

        Assert.True(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public void ShowAnError_WhenTheOldPasswordIsWrong_ForOnValidSubmitAsync()
    {
        var httpContext = new DefaultHttpContext();
        var user = new ApplicationUser { Id = "user-1" };
        (UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =
            RegisterServices(httpContext, user);
        userManager.ChangePasswordAsync(user, Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(
            IdentityResult.Failed(new IdentityError { Description = "Incorrect password." })));

        IRenderedComponent<ChangePassword> cut = Render<ChangePassword>(parameters => parameters
            .AddCascadingValue(httpContext));
        cut.Find("#Input\\.OldPassword").Change("wrong-password");
        cut.Find("#Input\\.NewPassword").Change("NewP@ssw0rd1");
        cut.Find("#Input\\.ConfirmPassword").Change("NewP@ssw0rd1");
        cut.Find("form").Submit();

        Assert.Contains("Incorrect password", cut.Markup, StringComparison.Ordinal);
        Assert.False(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    private (UserManager<ApplicationUser>, SignInManager<ApplicationUser>) RegisterServices(
        HttpContext httpContext, ApplicationUser user)
    {
        UserManager<ApplicationUser> userManager = FakeUserManagerFactory.CreateUserManager();
        userManager.GetUserAsync(httpContext.User).Returns(Task.FromResult<ApplicationUser?>(user));
        Services.AddSingleton(userManager);
        SignInManager<ApplicationUser> signInManager = FakeUserManagerFactory.CreateSignInManager(userManager);
        Services.AddSingleton(signInManager);
        Services.AddLogging();
        IdentityTestServices.RegisterIdentityRedirectManager(Services);
        return (userManager, signInManager);
    }
}
