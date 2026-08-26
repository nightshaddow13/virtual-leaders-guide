using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace VirtualLeadersGuide.Web.Identity;

/// <summary>
/// Options for <see cref="InviteTokenProvider"/> - a 7-day lifespan (frame 3c's "LINK EXPIRES IN 7 DAYS"),
/// distinct from <see cref="DataProtectorTokenProvider{TUser}"/>'s stock 1-day default that password-reset
/// keeps (P2-12, #43).
/// </summary>
public sealed class InviteTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InviteTokenProviderOptions()
    {
        Name = "InviteDataProtectorTokenProvider";
        TokenLifespan = TimeSpan.FromDays(7);
    }
}

/// <summary>
/// A second <see cref="DataProtectorTokenProvider{TUser}"/>, registered as <c>"Invite"</c>
/// alongside <see cref="AddDefaultTokenProviders"/>'s stock providers, so an invite's setup-password token
/// (7 days) can outlive a password-reset token (1 day, unchanged) without touching the latter (P2-12, #43).
/// </summary>
/// <remarks>
/// Generated with <c>UserManager.GenerateUserTokenAsync(user, "Invite", "SetPassword")</c>, verified with
/// <c>VerifyUserTokenAsync</c> - deliberately not <c>GeneratePasswordResetTokenAsync</c>/
/// <c>ResetPasswordAsync</c>, which are hardwired to <see cref="IdentityOptions.Tokens"/>'s
/// <c>PasswordResetTokenProvider</c> ("Default") and would use the 1-day lifespan regardless of this type
/// existing. <see cref="InviteTokenProviderOptions.Name"/> gives this provider its own Data Protection
/// purpose string, distinct from the stock provider's, so the two never validate each other's tokens.
/// </remarks>
public sealed class InviteTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InviteTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger);
