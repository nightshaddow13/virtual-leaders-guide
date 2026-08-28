using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VirtualLeadersGuide.Web.Components.Account.Pages.Manage;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

public class DeletePersonalDataShould : BunitContext
{
    [Fact]
    public void SignOutAndRedirect_WhenTheAccountHasNoPasswordToConfirm_ForOnValidSubmitAsync()
    {
        var httpContext = new DefaultHttpContext();
        var user = new ApplicationUser { Id = "user-1" };
        UserManager<ApplicationUser> userManager = RegisterServices(httpContext, user, requiresPassword: false);
        userManager.DeleteAsync(user).Returns(Task.FromResult(IdentityResult.Success));

        IRenderedComponent<DeletePersonalData> cut = Render<DeletePersonalData>(parameters => parameters
            .AddCascadingValue(httpContext));
        cut.Find("form").Submit();

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.NotEmpty(navigation.History);
    }

    [Fact]
    public void ShowAnErrorAndNotDelete_WhenThePasswordIsWrong_ForOnValidSubmitAsync()
    {
        var httpContext = new DefaultHttpContext();
        var user = new ApplicationUser { Id = "user-1" };
        UserManager<ApplicationUser> userManager = RegisterServices(httpContext, user, requiresPassword: true);
        userManager.CheckPasswordAsync(user, Arg.Any<string>()).Returns(Task.FromResult(false));

        IRenderedComponent<DeletePersonalData> cut = Render<DeletePersonalData>(parameters => parameters
            .AddCascadingValue(httpContext));
        cut.Find("#Input\\.Password").Change("wrong-password");
        cut.Find("form").Submit();

        Assert.Contains("Error: Incorrect password", cut.Markup, StringComparison.Ordinal);
        userManager.DidNotReceive().DeleteAsync(Arg.Any<ApplicationUser>());
    }

    private UserManager<ApplicationUser> RegisterServices(HttpContext httpContext, ApplicationUser user, bool requiresPassword)
    {
        UserManager<ApplicationUser> userManager = FakeUserManagerFactory.CreateUserManager();
        userManager.GetUserAsync(httpContext.User).Returns(Task.FromResult<ApplicationUser?>(user));
        userManager.HasPasswordAsync(user).Returns(Task.FromResult(requiresPassword));
        Services.AddSingleton(userManager);
        Services.AddSingleton(FakeUserManagerFactory.CreateSignInManager(userManager));
        Services.AddLogging();
        IdentityTestServices.RegisterIdentityRedirectManager(Services);
        return userManager;
    }
}
