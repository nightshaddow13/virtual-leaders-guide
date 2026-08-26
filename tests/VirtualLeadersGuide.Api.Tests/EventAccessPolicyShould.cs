using System.Security.Claims;
using VirtualLeadersGuide.Api.Authorization;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// Unit coverage for <see cref="EventAccessPolicy"/>'s claim parsing (P2-7, #16) - no host, no database;
/// <see cref="EventsResourceShould"/> covers the same rules end to end over <c>/api/events</c>.
/// </remarks>
public class EventAccessPolicyShould
{
    [Fact]
    public void GrantEverything_WhenTheCallerHoldsAnAdminClaim()
    {
        var policy = new EventAccessPolicy(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin })));

        Assert.True(policy.IsAdmin);
        Assert.True(policy.CanCreate);
        Assert.True(policy.CanDelete);
        Assert.True(policy.CanRead(Guid.NewGuid()));
        Assert.True(policy.CanUpdate(Guid.NewGuid()));
    }

    [Fact]
    public void GrantReadOnlyOnTheAssignedEvent_WhenTheCallerHoldsAScopedDirectorClaim()
    {
        var eventId = Guid.NewGuid();
        var policy = new EventAccessPolicy(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = eventId })));

        Assert.False(policy.IsAdmin);
        Assert.False(policy.CanCreate);
        Assert.False(policy.CanDelete);
        Assert.True(policy.CanRead(eventId));
        Assert.False(policy.CanUpdate(eventId));
        Assert.False(policy.CanRead(Guid.NewGuid()));
    }

    /// <remarks>
    /// Pins ADR-0035: an unscoped Director claim - the Role held with no Event, established by Invite
    /// (P2-12, #43) - grants nothing here by design, not by omission. A future reader must not "fix" this
    /// by adding a branch that treats a null-Event Director claim as platform-wide access.
    /// </remarks>
    [Fact]
    public void GrantNothing_WhenADirectorClaimCarriesNoEventScope()
    {
        var policy = new EventAccessPolicy(PrincipalWith(RoleNames.Director));

        Assert.False(policy.IsAdmin);
        Assert.Empty(policy.AssignedEventIds);
        Assert.False(policy.CanRead(Guid.NewGuid()));
        Assert.False(policy.CanUpdate(Guid.NewGuid()));
    }

    [Fact]
    public void GrantNothing_WhenADirectorClaimsEventScopeIsMalformed()
    {
        var policy = new EventAccessPolicy(PrincipalWith($"{RoleNames.Director}:not-a-guid"));

        Assert.False(policy.IsAdmin);
        Assert.Empty(policy.AssignedEventIds);
    }

    [Fact]
    public void GrantEveryAssignedEvent_WhenTheCallerHoldsMultipleDirectorClaims()
    {
        var firstEventId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();
        var policy = new EventAccessPolicy(PrincipalWith(
            RoleClaimValue.Format(new RoleGrantDto
            {
                Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = firstEventId
            }),
            RoleClaimValue.Format(new RoleGrantDto
            {
                Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = secondEventId
            })));

        Assert.Equal(new[] { firstEventId, secondEventId }.Order(), policy.AssignedEventIds.Order());
    }

    [Fact]
    public void GrantNothing_WhenTheCallerHoldsNoRoleClaims()
    {
        var policy = new EventAccessPolicy(PrincipalWith());

        Assert.False(policy.IsAdmin);
        Assert.Empty(policy.AssignedEventIds);
        Assert.False(policy.CanCreate);
        Assert.False(policy.CanDelete);
        Assert.False(policy.CanRead(Guid.NewGuid()));
        Assert.False(policy.CanUpdate(Guid.NewGuid()));
    }

    private static ClaimsPrincipal PrincipalWith(params string[] roleClaims)
    {
        var identity = new ClaimsIdentity(roleClaims.Select(value => new Claim(ClaimTypes.Role, value)));
        return new ClaimsPrincipal(identity);
    }
}
