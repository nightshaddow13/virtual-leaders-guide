using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VirtualLeadersGuide.Web.Directors;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Builds a real <see cref="DirectorInviteService"/> over fake dependencies, for
/// <c>UserDetail.razor</c>/<c>InviteDirectorDialog.razor</c> tests that need it without a real
/// <see cref="UserManager{TUser}"/> or Api behind it. <see cref="DirectorInviteService"/> itself is
/// <see langword="sealed"/> with no interface, so it can't be substituted directly - its own dependencies
/// are, instead. Its <see cref="NavigationManager"/> dependency is a standalone no-op, not the one bUnit
/// wires into the render tree - resolving a service from <c>BunitContext.Services</c> permanently locks it
/// against further registrations, and every test using this factory registers other services afterwards.
/// </remarks>
internal static class DirectorInviteServiceTestFactory
{
    public static DirectorInviteService Create(
        UserManager<ApplicationUser> userManager, HttpMessageHandler directorApiHandler) =>
        new(
            userManager,
            ApiClientTestFactory.CreateDirectorClient(directorApiHandler),
            Substitute.For<IInviteEmailSender>(),
            new NoOpNavigationManager(),
            NullLogger<DirectorInviteService>.Instance);

    private sealed class NoOpNavigationManager : NavigationManager
    {
        public NoOpNavigationManager() => Initialize("http://localhost/", "http://localhost/");
    }
}
