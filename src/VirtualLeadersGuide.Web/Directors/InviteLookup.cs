namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// Backs the invite modal's step 1 -&gt; 2A/2B fork (frame 3b): whether the typed email already belongs to
/// a platform User.
/// </summary>
/// <param name="ExistingUser">Set only when <see cref="IsExistingUser"/> - the account frame 3b's step 2B shows.</param>
public readonly record struct InviteLookup(bool IsExistingUser, UserRowDto? ExistingUser)
{
    public static InviteLookup NewEmail() => new(false, null);

    public static InviteLookup ExistingUserFound(UserRowDto user) => new(true, user);
}
