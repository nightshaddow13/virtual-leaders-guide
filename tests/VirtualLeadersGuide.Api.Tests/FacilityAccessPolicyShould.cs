using System.Security.Claims;
using VirtualLeadersGuide.Api.Authorization;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Unit coverage for <see cref="FacilityAccessPolicy"/>'s claim parsing (P8-2, #166) - no host, no database;
/// <see cref="FacilitiesResourceShould"/> covers the same rules end to end over <c>/api/facilities</c>.
/// Unlike <see cref="ActivityAccessPolicyShould"/>, every Director case here grants Read regardless of
/// Event scope (ADR-0070) - a Facility has no owning Event for a Director claim to be scoped against.
/// </remarks>
public class FacilityAccessPolicyShould
{
    [Fact]
    public void GrantReadAndWrite_WhenTheCallerHoldsAnAdminClaim()
    {
        var policy = new FacilityAccessPolicy(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin })));

        Assert.True(policy.IsAdmin);
        Assert.True(policy.CanRead);
        Assert.True(policy.CanWrite);
    }

    [Fact]
    public void GrantReadOnly_WhenTheCallerHoldsAnEventScopedDirectorClaim()
    {
        var policy = new FacilityAccessPolicy(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = Guid.NewGuid() })));

        Assert.False(policy.IsAdmin);
        Assert.True(policy.CanRead);
        Assert.False(policy.CanWrite);
    }

    [Fact]
    public void GrantReadOnly_WhenTheCallerHoldsAPlatformWideDirectorClaim()
    {
        var policy = new FacilityAccessPolicy(PrincipalWith(RoleNames.Director));

        Assert.False(policy.IsAdmin);
        Assert.True(policy.CanRead);
        Assert.False(policy.CanWrite);
    }

    [Fact]
    public void GrantNothing_WhenTheCallerHoldsNoRoleClaims()
    {
        var policy = new FacilityAccessPolicy(PrincipalWith());

        Assert.False(policy.IsAdmin);
        Assert.False(policy.CanRead);
        Assert.False(policy.CanWrite);
    }

    private static ClaimsPrincipal PrincipalWith(params string[] roleClaims)
    {
        var identity = new ClaimsIdentity(roleClaims.Select(value => new Claim(ClaimTypes.Role, value)));
        return new ClaimsPrincipal(identity);
    }
}
