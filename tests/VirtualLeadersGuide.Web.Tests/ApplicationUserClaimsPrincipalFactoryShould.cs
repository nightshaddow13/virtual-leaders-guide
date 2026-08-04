using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

// Exercises real DI wiring - AddIdentityCore<ApplicationUser>().AddUserStore<ApiUserStore>()
// .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>(), the same chain Program.cs registers -
// against a fake "Api" backend for the grants lookup, proving our own wiring cooperates correctly with the
// framework. Mirrors SignInShould's same rationale for the sign-in half of this chain.
public class ApplicationUserClaimsPrincipalFactoryShould
{
    [Fact]
    public async Task StampOneRoleClaimPerGrant_WhenGrantsExist_ForCreateAsync()
    {
        List<RoleGrantDto> grants =
        [
            new() { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin, EventId = null },
            new() { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = Guid.NewGuid() }
        ];
        var user = new ApplicationUser { Id = "user-1", UserName = "user@example.com" };

        ClaimsPrincipal principal = await CreatePrincipalAsync(user, grants);

        List<string> roleClaims = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Equal(2, roleClaims.Count);
        Assert.Contains(RoleClaimValue.Format(grants[0]), roleClaims);
        Assert.Contains(RoleClaimValue.Format(grants[1]), roleClaims);
    }

    [Fact]
    public async Task AddNoRoleClaims_WhenTheUserHoldsNoGrants_ForCreateAsync()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "user@example.com" };

        ClaimsPrincipal principal = await CreatePrincipalAsync(user, grants: []);

        Assert.Empty(principal.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task AddNoRoleClaims_WhenTheGrantsLookupReturnsNotFound_ForCreateAsync()
    {
        // grants: null - Api's row for this user is gone; ApplicationUserClaimsPrincipalFactory adds no role
        // claims rather than throwing (a genuine outage still surfaces as AuthorizationDataUnavailableException,
        // which ApiRoleGrantClient.GetGrantsAsync throws and this factory lets propagate).
        var user = new ApplicationUser { Id = "user-1", UserName = "user@example.com" };

        ClaimsPrincipal principal = await CreatePrincipalAsync(user, grants: null);

        Assert.Empty(principal.FindAll(ClaimTypes.Role));
    }

    private static async Task<ClaimsPrincipal> CreatePrincipalAsync(
        ApplicationUser user, IReadOnlyList<RoleGrantDto>? grants)
    {
        StubHttpMessageHandler handler = grants is null
            ? StubHttpMessageHandler.RespondingWith(HttpStatusCode.NotFound)
            : StubHttpMessageHandler.RespondingWithJson(HttpStatusCode.OK, grants);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("Api", client => client.BaseAddress = new Uri("https://api.internal/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddScoped<ApiRoleGrantClient>();
        services.AddIdentityCore<ApplicationUser>()
            .AddUserStore<ApiUserStore>()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        return await factory.CreateAsync(user);
    }
}
