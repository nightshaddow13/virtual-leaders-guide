using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VirtualLeadersGuide.Web.Components.Account.Pages;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <c>Login.razor</c>'s own branching in <c>LoginUser()</c> - <see cref="SignInShould"/> already
/// proves this app's real <see cref="SignInManager{TUser}"/>/<see cref="UserManager{TUser}"/> wiring works
/// end to end, driven directly against a synthetic <see cref="DefaultHttpContext"/> rather than through this
/// component (see that class's own remarks for why); this class is the component's own logic instead.
/// </remarks>
public class LoginShould : BunitContext
{
    [Fact]
    public void NavigateAway_WhenCredentialsAreValid_ForLoginUser()
    {
        SignInManager<ApplicationUser> signInManager = RegisterServices();
        signInManager.PasswordSignInAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Success));

        IRenderedComponent<Login> cut = Render<Login>(parameters => parameters
            .AddCascadingValue(new DefaultHttpContext { Request = { Method = "POST" } }));
        cut.Find("#Input\\.Email").Change("director@example.org");
        cut.Find("#Input\\.Password").Change("P@ssw0rd123!");
        cut.Find("form").Submit();

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.NotEmpty(navigation.History);
    }

    [Fact]
    public void ShowAnErrorAndStay_WhenCredentialsAreInvalid_ForLoginUser()
    {
        SignInManager<ApplicationUser> signInManager = RegisterServices();
        signInManager.PasswordSignInAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.Failed));

        IRenderedComponent<Login> cut = Render<Login>(parameters => parameters
            .AddCascadingValue(new DefaultHttpContext { Request = { Method = "POST" } }));
        cut.Find("#Input\\.Email").Change("director@example.org");
        cut.Find("#Input\\.Password").Change("wrong-password");
        cut.Find("form").Submit();

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Empty(navigation.History);
        Assert.Contains("Invalid login attempt", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigateToLockout_WhenTheAccountIsLockedOut_ForLoginUser()
    {
        SignInManager<ApplicationUser> signInManager = RegisterServices();
        signInManager.PasswordSignInAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Task.FromResult(SignInResult.LockedOut));

        IRenderedComponent<Login> cut = Render<Login>(parameters => parameters
            .AddCascadingValue(new DefaultHttpContext { Request = { Method = "POST" } }));
        cut.Find("#Input\\.Email").Change("director@example.org");
        cut.Find("#Input\\.Password").Change("P@ssw0rd123!");
        cut.Find("form").Submit();

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("Account/Lockout", navigation.History.Last().Uri, StringComparison.Ordinal);
    }

    private SignInManager<ApplicationUser> RegisterServices()
    {
        UserManager<ApplicationUser> userManager = FakeUserManagerFactory.CreateUserManager();
        SignInManager<ApplicationUser> signInManager = FakeUserManagerFactory.CreateSignInManager(userManager);
        Services.AddSingleton(signInManager);
        Services.AddLogging();
        IdentityTestServices.RegisterIdentityRedirectManager(Services);
        return signInManager;
    }
}
