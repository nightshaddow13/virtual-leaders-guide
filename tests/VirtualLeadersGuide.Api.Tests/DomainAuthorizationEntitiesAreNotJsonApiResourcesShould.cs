namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// <c>Role</c> (<c>VirtualLeadersGuide.Api.Data</c>) is a plain POCO, not <c>Identifiable&lt;T&gt;</c>, so it
/// isn't reachable as a JSON:API resource (ADR-0017's Consequences, unchanged by ADR-0033). <c>Event</c> was
/// the same until P2-7 (#16) turned it into a resource - see <c>EventsResourceShould</c> for its positive
/// coverage. <c>UserRole</c> was the same until P2-8 (#17; ADR-0033) turned it into a resource, exposed at
/// <c>/api/roleGrants</c> (not <c>/api/userRoles</c> - see <c>UserRole</c>'s <c>[Resource(PublicName = ...)]</c>)
/// - see <c>RoleGrantsResourceShould</c> for its positive coverage.
/// </remarks>
public class DomainAuthorizationEntitiesAreNotJsonApiResourcesShould : NonResourceEntityShouldBase
{
    [Fact]
    public Task ReturnNotFound_WhenRequestingRolesAsAJsonApiResource_ForGetCollection() =>
        AssertNotFoundAsync("/api/roles");
}
