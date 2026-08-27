using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Account.Pages;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// <c>Code</c> is a <see langword="private"/> <c>[SupplyParameterFromQuery]</c> property - bUnit's
/// parameter builder can only set a public one, so the "valid code, successful reset" branch isn't
/// reachable from a component test at all; only the no-code branch, which needs no parameter set, is.
/// </remarks>
public class ResetPasswordShould : BunitContext
{
    [Fact]
    public void RedirectToInvalidPasswordReset_WhenNoCodeWasSupplied_ForOnInitialized()
    {
        Services.AddSingleton(FakeUserManagerFactory.CreateUserManager());
        IdentityTestServices.RegisterIdentityRedirectManager(Services);

        // The child <StatusMessage> component unconditionally reads a cascading HttpContext in its own
        // OnInitialized, regardless of whether this page declares one itself.
        Render<ResetPassword>(parameters => parameters.AddCascadingValue(new DefaultHttpContext()));

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("Account/InvalidPasswordReset", navigation.History.Last().Uri, StringComparison.Ordinal);
    }
}
