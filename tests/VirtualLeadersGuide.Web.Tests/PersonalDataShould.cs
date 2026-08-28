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

public class PersonalDataShould : BunitContext
{
    [Fact]
    public void RedirectToInvalidUser_WhenTheSignedInUserCannotBeLoaded_ForOnInitializedAsync()
    {
        var httpContext = new DefaultHttpContext();
        UserManager<ApplicationUser> userManager = FakeUserManagerFactory.CreateUserManager();
        userManager.GetUserAsync(httpContext.User).Returns(Task.FromResult<ApplicationUser?>(null));
        Services.AddSingleton(userManager);
        IdentityTestServices.RegisterIdentityRedirectManager(Services);

        Render<PersonalData>(parameters => parameters.AddCascadingValue(httpContext));

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("Account/InvalidUser", navigation.History.Last().Uri, StringComparison.Ordinal);
    }
}
