using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Account;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Shared registration for the Account pages' <c>IdentityRedirectManager</c> dependency - reachable from
/// this test project only because of the <c>InternalsVisibleTo</c> in the Web project's own
/// <c>AssemblyInfo.cs</c> (ADR-0041); it's <see langword="internal sealed"/> with no public constructor
/// otherwise.
/// </remarks>
internal static class IdentityTestServices
{
    /// <remarks>
    /// Registered as a factory, not a built instance - resolving <see cref="NavigationManager"/> from
    /// <c>BunitContext.Services</c> permanently locks it against further registration (see
    /// <see cref="DirectorInviteServiceTestFactory"/>'s remarks for the same constraint), so building
    /// <c>IdentityRedirectManager</c> eagerly here would block any registration that comes after it. A
    /// factory defers that resolution until the render tree actually asks for the service.
    /// </remarks>
    public static void RegisterIdentityRedirectManager(IServiceCollection services) =>
        services.AddSingleton(sp => new IdentityRedirectManager(sp.GetRequiredService<NavigationManager>()));
}
