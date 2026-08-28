using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Components.Account.Pages;

public partial class SetupAccount
{
    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    [Inject]
    private UserManager<ApplicationUser> UserManager { get; set; } = default!;

    /// <remarks>
    /// Unlike <c>ResetPassword.razor</c>, the invitee never signed in and never typed their email - the
    /// link carries their <see cref="ApplicationUser.Id"/> (<see cref="UserId"/>) alongside the token
    /// (<see cref="Code"/>), see <c>DirectorInviteService.SendInviteEmailAsync</c>'s remarks. Verifying the
    /// token eagerly here (rather than only at submit, as <c>ResetPassword.razor</c> does) is deliberate -
    /// it's what lets this page show the invited email in <see cref="user"/>'s heading, and reject an
    /// expired/reused/tampered link before the invitee ever sees a form.
    /// </remarks>
    private IEnumerable<IdentityError>? identityErrors;
    private ApplicationUser? user;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "u")]
    private string? UserId { get; set; }

    [SupplyParameterFromQuery(Name = "t")]
    private string? Code { get; set; }

    private string? Message => identityErrors is null ? null : $"Error: {string.Join(", ", identityErrors.Select(error => error.Description))}";

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        if (UserId is null || Code is null)
        {
            RedirectManager.RedirectTo("Account/InvalidInvite");
            return;
        }

        Input.UserId = UserId;

        try
        {
            Input.Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));
        }
        catch (FormatException)
        {
            RedirectManager.RedirectTo("Account/InvalidInvite");
            return;
        }

        user = await UserManager.FindByIdAsync(Input.UserId);
        if (user is null
            || await UserManager.HasPasswordAsync(user)
            || !await UserManager.VerifyUserTokenAsync(user, "Invite", "SetPassword", Input.Code))
        {
            user = null;
            RedirectManager.RedirectTo("Account/InvalidInvite");
        }
    }

    /// <remarks>
    /// <see cref="user"/> and <see cref="InputModel.Code"/>'s validity were already established in
    /// <see cref="OnInitializedAsync"/> on this same request - a failed check there redirects away before
    /// this handler could ever run. <see cref="UserManager{TUser}.AddPasswordAsync"/> fails naturally (not
    /// re-checked here) if a password already exists, covering a link reused after activation completes in
    /// a race with this one.
    /// </remarks>
    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            RedirectManager.RedirectTo("Account/InvalidInvite");
            return;
        }

        IdentityResult result = await UserManager.AddPasswordAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            identityErrors = result.Errors;
            return;
        }

        user.EmailConfirmed = true;
        await UserManager.UpdateAsync(user);

        RedirectManager.RedirectToWithStatus(
            "Account/Login", "Your account is set up - sign in with your new password.", HttpContext);
    }

    private sealed class InputModel
    {
        [Required]
        public string UserId { get; set; } = "";

        [Required]
        public string Code { get; set; } = "";

        [Required]
        [PasswordLength]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
