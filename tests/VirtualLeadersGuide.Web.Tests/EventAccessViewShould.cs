using System.Security.Claims;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;

namespace VirtualLeadersGuide.Web.Tests;

public class EventAccessViewShould
{
    [Fact]
    public void GrantEverything_WhenTheCallerHoldsAnAdminClaim()
    {
        var eventId = Guid.NewGuid();
        var view = new EventAccessView(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Admin, RoleName = RoleNames.Admin })));

        Assert.True(view.IsAdmin);
        Assert.True(view.CanEditEventDetails);
        Assert.True(view.CanReadEvent(eventId));
        Assert.Empty(view.AssignedEventIds);
    }

    [Fact]
    public void GrantReadOnlyOnTheAssignedEvent_WhenTheCallerHoldsAScopedDirectorClaim()
    {
        var eventId = Guid.NewGuid();
        var view = new EventAccessView(PrincipalWith(RoleClaimValue.Format(
            new RoleGrantDto { Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = eventId })));

        Assert.False(view.IsAdmin);
        Assert.False(view.CanEditEventDetails);
        Assert.True(view.CanReadEvent(eventId));
        Assert.False(view.CanReadEvent(Guid.NewGuid()));
    }

    [Fact]
    public void GrantEveryAssignedEvent_WhenTheCallerHoldsMultipleDirectorClaims()
    {
        var firstEventId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();
        var view = new EventAccessView(PrincipalWith(
            RoleClaimValue.Format(new RoleGrantDto
            {
                Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = firstEventId
            }),
            RoleClaimValue.Format(new RoleGrantDto
            {
                Id = Guid.NewGuid(), RoleId = RoleIds.Director, RoleName = RoleNames.Director, EventId = secondEventId
            })));

        Assert.Equal(new[] { firstEventId, secondEventId }.Order(), view.AssignedEventIds.Order());
        Assert.False(view.CanEditEventDetails);
    }

    [Fact]
    public void GrantNothing_WhenTheCallerHoldsNoRoleClaims()
    {
        var view = new EventAccessView(PrincipalWith());

        Assert.False(view.IsAdmin);
        Assert.False(view.CanEditEventDetails);
        Assert.Empty(view.AssignedEventIds);
        Assert.False(view.CanReadEvent(Guid.NewGuid()));
    }

    private static ClaimsPrincipal PrincipalWith(params string[] roleClaims)
    {
        var identity = new ClaimsIdentity(roleClaims.Select(value => new Claim(ClaimTypes.Role, value)));
        return new ClaimsPrincipal(identity);
    }
}
