using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Components.Account.Pages.Manage;

public partial class Index
{
    [Inject]
    private UserManager<ApplicationUser> UserManager { get; set; } = default!;

    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private ApplicationUser? user;
    private string? username;
    private string? phoneNumber;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        user = await UserManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        username = await UserManager.GetUserNameAsync(user);
        phoneNumber = await UserManager.GetPhoneNumberAsync(user);

        Input.PhoneNumber ??= phoneNumber;
    }

    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        if (Input.PhoneNumber != phoneNumber)
        {
            var setPhoneResult = await UserManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
            if (!setPhoneResult.Succeeded)
            {
                RedirectManager.RedirectToCurrentPageWithStatus("Error: Failed to set phone number.", HttpContext);
                return;
            }
        }

        await SignInManager.RefreshSignInAsync(user);
        RedirectManager.RedirectToCurrentPageWithStatus("Your profile has been updated", HttpContext);
    }

    private sealed class InputModel
    {
        [Phone]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }
    }
}
