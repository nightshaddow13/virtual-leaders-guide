using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VirtualLeadersGuide.Web.Components.Account.Pages;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

public class ForgotPasswordShould : BunitContext
{
    [Fact]
    public void SendAResetLinkAndRedirect_WhenTheEmailBelongsToAConfirmedUser_ForOnValidSubmitAsync()
    {
        IEmailSender<ApplicationUser> emailSender = RegisterServices(out UserManager<ApplicationUser> userManager);
        var user = new ApplicationUser { Id = "user-1", Email = "director@example.org" };
        userManager.FindByEmailAsync("director@example.org").Returns(Task.FromResult<ApplicationUser?>(user));
        userManager.IsEmailConfirmedAsync(user).Returns(Task.FromResult(true));
        userManager.GeneratePasswordResetTokenAsync(user).Returns(Task.FromResult("raw-token"));

        IRenderedComponent<ForgotPassword> cut = Render<ForgotPassword>();
        cut.Find("#Input\\.Email").Change("director@example.org");
        cut.Find("form").Submit();

        emailSender.Received(1).SendPasswordResetLinkAsync(user, "director@example.org", Arg.Any<string>());
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("Account/ForgotPasswordConfirmation", navigation.History.Last().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void RedirectWithoutSendingAnEmail_WhenNoAccountMatchesTheEmail_ForOnValidSubmitAsync()
    {
        IEmailSender<ApplicationUser> emailSender = RegisterServices(out UserManager<ApplicationUser> userManager);
        userManager.FindByEmailAsync(Arg.Any<string>()).Returns(Task.FromResult<ApplicationUser?>(null));

        IRenderedComponent<ForgotPassword> cut = Render<ForgotPassword>();
        cut.Find("#Input\\.Email").Change("nobody@example.org");
        cut.Find("form").Submit();

        emailSender.DidNotReceive().SendPasswordResetLinkAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("Account/ForgotPasswordConfirmation", navigation.History.Last().Uri, StringComparison.Ordinal);
    }

    private IEmailSender<ApplicationUser> RegisterServices(out UserManager<ApplicationUser> userManager)
    {
        userManager = FakeUserManagerFactory.CreateUserManager();
        Services.AddSingleton(userManager);
        IEmailSender<ApplicationUser> emailSender = Substitute.For<IEmailSender<ApplicationUser>>();
        Services.AddSingleton(emailSender);
        IdentityTestServices.RegisterIdentityRedirectManager(Services);
        return emailSender;
    }
}
