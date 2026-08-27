using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// <see cref="UserManager{TUser}"/>/<see cref="SignInManager{TUser}"/> have no interface, but ASP.NET Core
/// Identity makes their members <see langword="virtual"/> specifically so a substitute can override them
/// directly - no real <see cref="IUserStore{TUser}"/> or DI container needed behind it. Every constructor
/// argument a test doesn't care about is a bare NSubstitute stand-in or left <see langword="null"/>;
/// <see cref="UserManager{TUser}"/>'s own constructor defaults the ones that need one (<c>Options</c>,
/// <c>KeyNormalizer</c>, <c>ErrorDescriber</c>), and nothing here calls a method that would fall through to
/// the real <see cref="IUserStore{TUser}"/> - every test configures the manager's own method directly via
/// <c>.Returns(...)</c> instead.
/// </remarks>
internal static class FakeUserManagerFactory
{
    public static UserManager<ApplicationUser> CreateUserManager() =>
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

    public static SignInManager<ApplicationUser> CreateSignInManager(UserManager<ApplicationUser> userManager) =>
        Substitute.For<SignInManager<ApplicationUser>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null, null, null, null);
}
