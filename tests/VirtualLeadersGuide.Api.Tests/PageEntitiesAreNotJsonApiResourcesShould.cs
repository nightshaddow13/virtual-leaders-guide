namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// <c>Page</c>/<c>InfoPage</c>/<c>PageType</c> (<c>VirtualLeadersGuide.Api.Data</c>, P5-15, #20) are plain
/// POCOs, not <c>Identifiable&lt;T&gt;</c>, so none is reachable as a JSON:API resource - deliberately, since
/// <c>AddJsonApi&lt;TDbContext&gt;</c> walks every entity type in the EF model and would auto-register any
/// <c>IIdentifiable</c> type it finds regardless of a missing <c>[Resource]</c> attribute (see <c>Page</c>'s
/// remarks). P5-16 (#21) is what turns <c>InfoPage</c> into a resource - see the pattern
/// <see cref="DomainAuthorizationEntitiesAreNotJsonApiResourcesShould"/> already set for
/// <c>Event</c>/<c>UserRole</c> going the other way.
/// </remarks>
public class PageEntitiesAreNotJsonApiResourcesShould : NonResourceEntityShouldBase
{
    [Theory]
    [InlineData("/api/pages")]
    [InlineData("/api/infoPages")]
    [InlineData("/api/pageTypes")]
    public Task ReturnNotFound_WhenRequestingAsAJsonApiResource_ForGetCollection(string requestUri) =>
        AssertNotFoundAsync(requestUri);
}
