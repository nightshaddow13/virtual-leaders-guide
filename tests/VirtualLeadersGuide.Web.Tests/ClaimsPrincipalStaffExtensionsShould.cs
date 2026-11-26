using System.Security.Claims;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Authorization;

namespace VirtualLeadersGuide.Web.Tests;

public class ClaimsPrincipalStaffExtensionsShould
{
    [Theory]
    [InlineData(RoleIds.Admin, RoleNames.Admin)]
    [InlineData(RoleIds.Director, RoleNames.Director)]
    public void ReturnTrue_WhenThePrincipalHoldsARoleWithNoEvent_ForIsStaff(int roleId, string roleName)
    {
        var grant = new RoleGrantDto { Id = Guid.NewGuid(), RoleId = roleId, RoleName = roleName };
        var roleClaim = new Claim(ClaimTypes.Role, RoleClaimValue.Format(grant));

        Assert.True(SignedInWith(roleClaim).IsStaff());
    }

    [Fact]
    public void ReturnFalse_WhenThePrincipalIsAuthenticatedWithNoRoleClaim_ForIsStaff() =>
        Assert.False(SignedInWith(new Claim(ClaimTypes.NameIdentifier, "test-user-id")).IsStaff());

    [Fact]
    public void ReturnFalse_WhenThePrincipalIsAnonymous_ForIsStaff() =>
        Assert.False(new ClaimsPrincipal(new ClaimsIdentity()).IsStaff());

    private static ClaimsPrincipal SignedInWith(Claim claim) => new(new ClaimsIdentity([claim], "Test"));
}
