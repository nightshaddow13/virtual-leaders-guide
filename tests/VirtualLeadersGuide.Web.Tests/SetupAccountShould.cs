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
/// <c>UserId</c>/<c>Code</c> are <see langword="private"/> <c>[SupplyParameterFromQuery]</c> properties -
/// bUnit's parameter builder can only set a public one, so only the missing-parameters branch (which needs
/// neither set) is reachable from a component test; the valid-invite and expired/tampered-token branches
/// are not.
/// </remarks>
public class SetupAccountShould : BunitContext
{
    [Fact]
    public void RedirectToInvalidInvite_WhenNoInviteParametersWereSupplied_ForOnInitializedAsync()
    {
        Services.AddSingleton(FakeUserManagerFactory.CreateUserManager());
        IdentityTestServices.RegisterIdentityRedirectManager(Services);

        // The child <StatusMessage> component unconditionally reads a cascading HttpContext.
        Render<SetupAccount>(parameters => parameters.AddCascadingValue(new DefaultHttpContext()));

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.Contains("Account/InvalidInvite", navigation.History.Last().Uri, StringComparison.Ordinal);
    }
}
