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
/// The child <c>&lt;StatusMessage&gt;</c> component also unconditionally reads a cascading
/// <see cref="HttpContext"/> in its own <c>OnInitialized</c>, regardless of whether the host page declares
/// one itself - supplied below even though <c>ResetPassword</c> has no <see cref="HttpContext"/> dependency
/// of its own. <see cref="SetupAccountShould"/> supplies one for the same reason.
/// </remarks>
public class ResetPasswordShould : BunitContext
{
    [Fact]
    public void RedirectToInvalidPasswordReset_WhenNoCodeWasSupplied_ForOnInitialized()
    {
        Services.AddSingleton(FakeUserManagerFactory.CreateUserManager());
        IdentityTestServices.RegisterIdentityRedirectManager(Services);

        Render<ResetPassword>(parameters => parameters.AddCascadingValue(new DefaultHttpContext()));

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("Account/InvalidPasswordReset", navigation.History.Last().Uri, StringComparison.Ordinal);
    }
}
