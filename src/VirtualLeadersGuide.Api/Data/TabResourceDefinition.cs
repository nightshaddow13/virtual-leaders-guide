using System.Collections.Immutable;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;
using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Authorization;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Enforces Admin/Director read scoping on <c>/api/tabs</c> (P5-11, #96): an <c>Admin</c> sees every Tab; a
/// <c>Director</c> sees only Tabs on Events in <see cref="ActivityPlacementAccessPolicy.AssignedEventIds"/>.
/// </summary>
/// <remarks>
/// Read-only by construction - <see cref="Tab"/> carries <see cref="JsonApiDotNetCore.Controllers.JsonApiEndpoints.Query"/>
/// only, so there is no write operation to authorize here (ADR-0072). <see cref="OnApplyFilter"/> mirrors
/// <see cref="ActivityResourceDefinition.OnApplyFilter"/>'s collection-vs-single-resource asymmetry exactly -
/// a collection request is silently narrowed, a single-resource request outside the caller's access throws
/// 403 (ADR-0031).
/// </remarks>
public sealed class TabResourceDefinition : JsonApiResourceDefinition<Tab, Guid>
{
    /// <remarks>See <see cref="ActivityResourceDefinition.NoEventSentinel"/> for why this sentinel is safe.</remarks>
    private static readonly Guid NoEventSentinel = Guid.Empty;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;

    /// <summary>Constructs the definition with the services it needs to authorize Tab reads.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> for <see cref="CurrentPolicy"/>.</param>
    /// <param name="dbContext">Backs <see cref="StoredEventId"/>'s re-read.</param>
    public TabResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        var policy = CurrentPolicy();
        if (policy.IsAdmin)
        {
            return existingFilter;
        }

        IJsonApiRequest request = JsonApiResourceDefinitionHelpers.GetRequest(_httpContextAccessor, nameof(TabResourceDefinition));
        if (request.PrimaryId is not null)
        {
            if (!policy.CanRead(StoredEventId(Guid.Parse(request.PrimaryId))))
            {
                throw ForbiddenException();
            }

            return existingFilter;
        }

        return JsonApiResourceDefinitionHelpers.And(existingFilter, BuildAssignedEventsFilter(policy));
    }

    private FilterExpression BuildAssignedEventsFilter(ActivityPlacementAccessPolicy policy)
    {
        AttrAttribute eventIdAttribute = ResourceType.GetAttributeByPropertyName(nameof(Tab.EventId));
        var eventIdChain = new ResourceFieldChainExpression(eventIdAttribute);

        IImmutableSet<LiteralConstantExpression> constants = policy.AssignedEventIds.Count == 0
            ? ImmutableHashSet.Create(new LiteralConstantExpression(NoEventSentinel))
            : policy.AssignedEventIds
                .Select(eventId => new LiteralConstantExpression(eventId))
                .ToImmutableHashSet();

        return new AnyExpression(eventIdChain, constants);
    }

    /// <remarks>A missing row resolves to <see cref="NoEventSentinel"/>, failing every non-Admin's read - see <see cref="ActivityResourceDefinition.StoredEventId"/>'s identical reasoning.</remarks>
    private Guid StoredEventId(Guid tabId) =>
        _dbContext.Tabs.AsNoTracking()
            .Where(tab => tab.Id == tabId)
            .Select(tab => tab.EventId)
            .FirstOrDefault();

    private ActivityPlacementAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "TabResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException() =>
        JsonApiResourceDefinitionHelpers.ForbiddenException("You do not have permission to access this Tab.");
}
