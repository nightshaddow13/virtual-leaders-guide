using JsonApiDotNetCore.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Api.Data;

namespace VirtualLeadersGuide.Api.Tests;

/// <remarks>
/// <c>PageType</c> (<c>VirtualLeadersGuide.Api.Data</c>) is a plain POCO, not <c>Identifiable&lt;T&gt;</c>, so
/// it isn't reachable as a JSON:API resource - same posture as <c>Role</c> (ADR-0017's Consequences). <c>Page</c>
/// has been <c>Identifiable&lt;Guid&gt;</c> since P5-16 (#21), but is still unreachable, for two independent
/// reasons: it carries no <c>[Resource]</c>, so JsonApiDotNetCore's source generator emits no controller for
/// it, and <c>Program.cs</c> additionally removes it from the resource graph entirely (ADR-0059) - a
/// <c>[Resource]</c> attribute is what generates a controller, not resource-graph membership by itself, and
/// ADR-0055's original claim to the contrary was wrong. <c>InfoPage</c> was the same until P5-16 turned it
/// into a resource at <c>/api/infoPages</c> - see <c>InfoPagesResourceShould</c> for its positive coverage.
/// </remarks>
/// <remarks>
/// The 404 probe in <see cref="ReturnNotFound_WhenRequestingAsAJsonApiResource_ForGetCollection"/> can't
/// distinguish "removed from the resource graph" from "never had a controller" - both look identical over
/// HTTP. <see cref="RemovePageFromTheResourceGraph_WhileKeepingInfoPage_WhenTheApiStarts"/> pins the
/// graph-membership half of ADR-0059's decision directly, so a future change that stops calling
/// <c>Remove&lt;Page&gt;()</c> fails there even though <c>/api/pages</c> would still 404 (no <c>[Resource]</c>
/// on <c>Page</c> either way).
/// </remarks>
public class PageEntitiesAreNotJsonApiResourcesShould : NonResourceEntityShouldBase
{
    [Theory]
    [InlineData("/api/pages")]
    [InlineData("/api/pageTypes")]
    public Task ReturnNotFound_WhenRequestingAsAJsonApiResource_ForGetCollection(string requestUri) =>
        AssertNotFoundAsync(requestUri);

    [Fact]
    public void RemovePageFromTheResourceGraph_WhileKeepingInfoPage_WhenTheApiStarts()
    {
        var resourceGraph = Services.GetRequiredService<IResourceGraph>();

        Assert.Null(resourceGraph.FindResourceType(typeof(Page)));
        Assert.NotNull(resourceGraph.FindResourceType(typeof(InfoPage)));
    }
}
