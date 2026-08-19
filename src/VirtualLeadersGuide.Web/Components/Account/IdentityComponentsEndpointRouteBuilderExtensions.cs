using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VirtualLeadersGuide.Web.Identity;

namespace Microsoft.AspNetCore.Routing;

/// <summary>Endpoints required by the Identity Razor components in <c>/Components/Account/Pages</c>.</summary>
/// <remarks>
/// Trimmed from the <c>dotnet new blazor -au Individual -int Server</c> scaffold: dropped
/// <c>PerformExternalLogin</c>, <c>PasskeyCreationOptions</c>/<c>PasskeyRequestOptions</c>, and
/// <c>LinkExternalLogin</c> - external logins and passkeys aren't wired up anywhere in this app (ADR-0019:
/// password-only sign-in is deliberate; ADR-0022 cuts 2FA similarly). Kept <c>Logout</c> and
/// <c>DownloadPersonalData</c>, the latter trimmed to drop the <c>AuthenticatorKey</c>/external-login-provider
/// export lines - <c>ApiUserStore</c> implements neither <c>IUserAuthenticatorKeyStore</c> nor
/// <c>IUserLoginStore</c>, so calling those <c>UserManager</c> methods would throw
/// <see cref="NotSupportedException"/>.
/// </remarks>
internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        var manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization();

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

        manageGroup.MapPost("/DownloadPersonalData", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
            }

            var userId = await userManager.GetUserIdAsync(user);
            downloadLogger.LogInformation("User with ID '{UserId}' asked for their personal data.", userId);

            var personalData = new Dictionary<string, string>();
            var personalDataProps = typeof(ApplicationUser).GetProperties().Where(
                prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
            foreach (var p in personalDataProps)
            {
                personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
            }

            var fileBytes = JsonSerializer.SerializeToUtf8Bytes(personalData);

            context.Response.Headers.TryAdd("Content-Disposition", "attachment; filename=PersonalData.json");
            return TypedResults.File(fileBytes, contentType: "application/json", fileDownloadName: "PersonalData.json");
        });

        return accountGroup;
    }
}
