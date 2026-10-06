using System.Security.Claims;
using VirtualLeadersGuide.Api.Authorization;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Unit coverage for <see cref="ActivityAccessPolicy"/>'s claim parsing (P5-6, #87) - no host, no database;
/// <see cref="ActivitiesResourceShould"/> covers the same rules end to end over <c>/api/activities</c>.
/// <see cref="GrantNothing_WhenADirectorClaimCarriesNoEventScope"/> pins ADR-0035, the same trap
/// <see cref="EventAccessPolicyShould"/> already tests for the type this policy delegates to: an unscoped
/// Director claim grants nothing here by design, not by omission.
/// </remarks>
public class ActivityAccessPolicyShould
{
    [Fact]
    public void GrantReadAndWriteOnEveryEvent_WhenTheCallerHoldsAnAdminClaim()
    {
        var policy = new ActivityAccessPolicy(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin })));

        Assert.True(policy.IsAdmin);
        Assert.True(policy.CanRead(Guid.NewGuid()));
        Assert.True(policy.CanWrite(Guid.NewGuid()));
    }

    [Fact]
    public void GrantReadAndWriteOnTheAssignedEvent_WhenTheCallerHoldsAScopedDirectorClaim()
    {
        var eventId = Guid.NewGuid();
        var policy = new ActivityAccessPolicy(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = eventId })));

        Assert.False(policy.IsAdmin);
        Assert.True(policy.CanRead(eventId));
        Assert.True(policy.CanWrite(eventId));
        Assert.False(policy.CanRead(Guid.NewGuid()));
        Assert.False(policy.CanWrite(Guid.NewGuid()));
    }

    [Fact]
    public void GrantNothing_WhenADirectorClaimCarriesNoEventScope()
    {
        var policy = new ActivityAccessPolicy(PrincipalWith(RoleNames.Director));

        Assert.False(policy.IsAdmin);
        Assert.Empty(policy.AssignedEventIds);
        Assert.False(policy.CanRead(Guid.NewGuid()));
        Assert.False(policy.CanWrite(Guid.NewGuid()));
    }

    [Fact]
    public void GrantNothing_WhenTheCallerHoldsNoRoleClaims()
    {
        var policy = new ActivityAccessPolicy(PrincipalWith());

        Assert.False(policy.IsAdmin);
        Assert.Empty(policy.AssignedEventIds);
        Assert.False(policy.CanRead(Guid.NewGuid()));
        Assert.False(policy.CanWrite(Guid.NewGuid()));
    }

    private static ClaimsPrincipal PrincipalWith(params string[] roleClaims)
    {
        var identity = new ClaimsIdentity(roleClaims.Select(value => new Claim(ClaimTypes.Role, value)));
        return new ClaimsPrincipal(identity);
    }
}
