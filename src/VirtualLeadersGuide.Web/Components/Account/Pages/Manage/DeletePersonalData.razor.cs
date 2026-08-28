using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Components.Account.Pages.Manage;

public partial class DeletePersonalData
{
    [Inject]
    private UserManager<ApplicationUser> UserManager { get; set; } = default!;

    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    [Inject]
    private ILogger<DeletePersonalData> Logger { get; set; } = default!;

    private string? message;
    private ApplicationUser? user;
    private bool requirePassword;

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
        requirePassword = await UserManager.HasPasswordAsync(user);
    }

    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        if (requirePassword && !await UserManager.CheckPasswordAsync(user, Input.Password))
        {
            message = "Error: Incorrect password.";
            return;
        }

        var result = await UserManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Unexpected error occurred deleting user.");
        }

        await SignInManager.SignOutAsync();

        var userId = await UserManager.GetUserIdAsync(user);
        Logger.LogInformation("User with ID '{UserId}' deleted themselves.", userId);

        RedirectManager.RedirectToCurrentPage();
    }

    private sealed class InputModel
    {
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }
}
