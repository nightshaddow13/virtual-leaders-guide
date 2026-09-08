using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// Owns the whole Director-invite lifecycle (P2-12, #43) - invite, resend, revoke - so no Razor page has to
/// sequence <see cref="UserManager{TUser}"/>, <see cref="ApiDirectorClient"/>, and
/// <see cref="IInviteEmailSender"/> by hand.
/// </summary>
/// <remarks>
/// A person's Director Role is established exactly once, here, by <see cref="InviteAsync"/> - never from
/// the Event page (ADR-0035). The invite token is minted with the <c>"Invite"</c> provider
/// (<see cref="InviteTokenProvider"/>, 7-day lifespan), not <c>GeneratePasswordResetTokenAsync</c>/
/// <c>ResetPasswordAsync</c>, which are hardwired to the stock 1-day provider regardless of what else is
/// registered.
/// </remarks>
public sealed class DirectorInviteService(
    UserManager<ApplicationUser> userManager,
    ApiDirectorClient directorClient,
    IInviteEmailSender emailSender,
    NavigationManager navigationManager,
    ILogger<DirectorInviteService> logger)
{
    /// <summary>Backs the invite modal's step 1 -&gt; 2A/2B fork (frame 3b).</summary>
    public async Task<InviteLookup> LookUpAsync(string email, CancellationToken cancellationToken)
    {
        ApplicationUser? existing = await userManager.FindByEmailAsync(email);
        if (existing is null)
        {
            return InviteLookup.NewEmail();
        }

        UserRowDto? row = await directorClient.GetUserAsync(existing.Id, cancellationToken);
        return row is null ? InviteLookup.NewEmail() : InviteLookup.ExistingUserFound(row);
    }

    /// <summary>
    /// Invites <paramref name="email"/> as a Director: creates their passwordless User row, grants them the
    /// unscoped Director Role (ADR-0035 - no Event picker here, by design, frame 3b), and emails a
    /// 7-day password-setup link.
    /// </summary>
    /// <param name="email">The address to invite.</param>
    /// <param name="displayName">
    /// The name the Admin optionally supplies at invite time (frame 3b's step 2A) - the invitee never sets
    /// this themselves (see <c>SetupAccount.razor</c>).
    /// </param>
    /// <param name="cancellationToken">Propagated to Api calls.</param>
    /// <returns>
    /// <see cref="InviteOutcome.AlreadyOnPlatform"/> if the email already belongs to a User (AC 4) - the
    /// caller should route to <see cref="LookUpAsync"/>'s result instead of retrying this.
    /// </returns>
    /// <remarks>
    /// A concurrent invite for the same email can lose the race between the <see cref="UserManager{TUser}.FindByEmailAsync"/>
    /// check above and <see cref="UserManager{TUser}.CreateAsync(TUser)"/> below - <c>ApiUserStore.CreateAsync</c>
    /// surfaces that as a duplicate-username failure, which this method also reports as
    /// <see cref="InviteOutcome.AlreadyOnPlatform"/> rather than <see cref="InviteOutcome.StoreUnavailable"/>.
    /// </remarks>
    public async Task<InviteOutcome> InviteAsync(
        string email, string? displayName, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return InviteOutcome.AlreadyOnPlatform;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            EmailConfirmed = false
        };

        IdentityResult createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            return createResult.Errors.Any(error => error.Code == "DuplicateUserName")
                ? InviteOutcome.AlreadyOnPlatform
                : InviteOutcome.StoreUnavailable;
        }

        try
        {
            GrantWriteOutcome grantOutcome = await directorClient.GrantDirectorRoleAsync(user.Id, cancellationToken);
            if (grantOutcome is not (GrantWriteOutcome.Created or GrantWriteOutcome.AlreadyGranted))
            {
                logger.LogWarning(
                    "Rolling back invite for a new User: granting the Director Role returned {Outcome}.", grantOutcome);
                await userManager.DeleteAsync(user);
                return InviteOutcome.StoreUnavailable;
            }

            await SendInviteEmailAsync(user);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rolling back invite for a new User: granting the Role or sending the email failed.");
            await userManager.DeleteAsync(user);
            return InviteOutcome.StoreUnavailable;
        }

        return InviteOutcome.Invited;
    }

    /// <summary>Re-sends a pending invite's setup email, rotating the security stamp so any prior link stops working.</summary>
    public async Task<ResendOutcome> ResendAsync(string userId, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return ResendOutcome.NotFound;
        }

        if (await userManager.HasPasswordAsync(user))
        {
            return ResendOutcome.AlreadyActive;
        }

        await userManager.UpdateSecurityStampAsync(user);

        try
        {
            await SendInviteEmailAsync(user);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Resend for User {UserId} rotated the security stamp but sending the email failed.", userId);
            return ResendOutcome.SendFailed;
        }

        return ResendOutcome.Sent;
    }

    /// <summary>
    /// Revokes an un-activated Invite outright (ADR-0035): deletes the <c>ApplicationUser</c> row, and the
    /// database's own cascade (<c>VirtualLeadersGuideDbContext.ConfigureUserRoles</c>) removes its
    /// <c>UserRole</c> rows - the unscoped Role, and any Event Grants assigned before activation.
    /// </summary>
    public async Task<RevokeOutcome> RevokeAsync(string userId, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return RevokeOutcome.NotFound;
        }

        if (await userManager.HasPasswordAsync(user))
        {
            return RevokeOutcome.AlreadyActive;
        }

        await userManager.DeleteAsync(user);
        return RevokeOutcome.Revoked;
    }

    /// <summary>
    /// Deletes a User's account outright (P2-19, #114): the <c>ApplicationUser</c> row, and the database's
    /// own cascade removes its <c>UserRole</c> rows - the Director Role, and every Event-scoped Grant.
    /// </summary>
    /// <param name="userId">The target User's id.</param>
    /// <param name="callerUserId">The signed-in Admin's own id, for <see cref="UserDeleteOutcome.TargetIsSelf"/>.</param>
    /// <param name="cancellationToken">Propagated to Api calls.</param>
    /// <remarks>
    /// Enforces ADR-0045's two guard rules itself, not just at the UI layer - a stale page can still reach
    /// this call after its Danger zone button was rendered enabled. Unlike <see cref="RevokeAsync"/>, this
    /// has no activation gate: it deletes an un-activated Invite's row just as readily as an activated
    /// User's - the Users screen is what restricts Delete to the ACTIVE branch and Revoke to the INVITED
    /// one, not this method. A caught <see cref="DirectorDataUnavailableException"/> from the Admin-check
    /// fetch returns <see cref="UserDeleteOutcome.StoreUnavailable"/> rather than proceeding without
    /// knowing whether the target holds Admin, matching <see cref="InviteAsync"/>'s own degrade-on-failure
    /// shape.
    /// </remarks>
    public async Task<UserDeleteOutcome> DeleteAsync(
        string userId, string callerUserId, CancellationToken cancellationToken)
    {
        if (userId == callerUserId)
        {
            return UserDeleteOutcome.TargetIsSelf;
        }

        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return UserDeleteOutcome.NotFound;
        }

        UserRowDto? row;
        try
        {
            row = await directorClient.GetUserAsync(userId, cancellationToken);
        }
        catch (DirectorDataUnavailableException)
        {
            return UserDeleteOutcome.StoreUnavailable;
        }

        if (row is null)
        {
            return UserDeleteOutcome.NotFound;
        }

        if (row.IsAdmin)
        {
            return UserDeleteOutcome.TargetIsAdmin;
        }

        await userManager.DeleteAsync(user);
        return UserDeleteOutcome.Deleted;
    }

    /// <remarks>
    /// Mirrors <c>ForgotPassword.razor</c>'s own link-generation: <c>GenerateUserTokenAsync</c> (the
    /// <c>"Invite"</c> provider, not <c>GeneratePasswordResetTokenAsync</c>) -&gt; Base64Url -&gt;
    /// <c>/setup</c> query string -&gt; HTML-encoded before handing it to the sender, the same discipline
    /// that makes <c>AcsEmailSender</c>'s single-quoted <c>href</c> interpolation safe. Unlike
    /// <c>ForgotPassword.razor</c>'s link, this one also carries <c>u</c> (the User's id) - verifying a
    /// token requires the specific <see cref="ApplicationUser"/> it was minted for
    /// (<c>VerifyUserTokenAsync</c> takes the user, not just the token), and unlike password-reset there's
    /// no session/cookie identifying who's clicking; the invitee has never signed in. This is also what
    /// lets <c>SetupAccount.razor</c> show the invited email without asking the invitee to retype it.
    /// </remarks>
    private async Task SendInviteEmailAsync(ApplicationUser user)
    {
        string token = await userManager.GenerateUserTokenAsync(user, "Invite", "SetPassword");
        string code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        string setupLink = navigationManager.GetUriWithQueryParameters(
            navigationManager.ToAbsoluteUri("/setup").AbsoluteUri,
            new Dictionary<string, object?> { ["u"] = user.Id, ["t"] = code });

        await emailSender.SendDirectorInviteAsync(user, user.Email!, HtmlEncoder.Default.Encode(setupLink));
    }
}
