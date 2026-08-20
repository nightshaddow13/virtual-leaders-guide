using System.Security.Claims;
using VirtualLeadersGuide.Api.Authorization;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Unit coverage for <see cref="RoleGrantAccessPolicy"/>'s claim parsing (P2-8, #17) - no host, no database;
/// <see cref="RoleGrantsResourceShould"/> covers the same rules end to end over <c>/api/roleGrants</c>. Shares
/// its claim-parsing coverage with <see cref="EventAccessPolicyShould"/> via the two policies' common
/// <c>RoleClaims.Parse</c>.
/// </remarks>
public class RoleGrantAccessPolicyShould
{
    [Fact]
    public void GrantReadAndWrite_WhenTheCallerHoldsAnAdminClaim()
    {
        var policy = new RoleGrantAccessPolicy(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin })));

        Assert.True(policy.IsAdmin);
        Assert.True(policy.CanRead);
        Assert.True(policy.CanWrite(RoleIds.Director));
    }

    [Fact]
    public void DenyWritingAnAdminGrant_WhenTheCallerHoldsAnAdminClaim()
    {
        var policy = new RoleGrantAccessPolicy(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin })));

        Assert.True(policy.CanRead);
        Assert.False(policy.CanWrite(RoleIds.Admin));
    }

    [Fact]
    public void GrantNothing_WhenTheCallerHoldsAScopedDirectorClaim()
    {
        var policy = new RoleGrantAccessPolicy(PrincipalWith(RoleClaimValue.Format(new RoleGrantDto
        {
            Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = Guid.NewGuid()
        })));

        Assert.False(policy.IsAdmin);
        Assert.False(policy.CanRead);
        Assert.False(policy.CanWrite(RoleIds.Director));
    }

    [Fact]
    public void GrantNothing_WhenTheAdminClaimCarriesAMalformedEventScope()
    {
        var policy = new RoleGrantAccessPolicy(PrincipalWith($"{RoleNames.Admin}:not-a-guid"));

        Assert.False(policy.IsAdmin);
        Assert.False(policy.CanRead);
    }

    [Fact]
    public void GrantNothing_WhenTheCallerHoldsNoRoleClaims()
    {
        var policy = new RoleGrantAccessPolicy(PrincipalWith());

        Assert.False(policy.IsAdmin);
        Assert.False(policy.CanRead);
        Assert.False(policy.CanWrite(RoleIds.Director));
    }

    private static ClaimsPrincipal PrincipalWith(params string[] roleClaims)
    {
        var identity = new ClaimsIdentity(roleClaims.Select(value => new Claim(ClaimTypes.Role, value)));
        return new ClaimsPrincipal(identity);
    }
}
