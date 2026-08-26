using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Serialization.Objects;
using VirtualLeadersGuide.Api.Authorization;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Enforces Admin-only access on <c>/api/users</c> (P2-12, #43): only an Admin may list or read an
/// <see cref="ApplicationUser"/> row through this resource.
/// </summary>
/// <remarks>
/// Before this ticket, <c>/api/users</c> had no per-row authorization beyond the blanket internal-JWT
/// policy on <c>MapControllers()</c> - any caller with a valid token could list every user's email. The
/// P2-12 Users screen reads this endpoint to render invited/active Directors and Admins, which is exactly
/// the surface that needed gating. Same rule as <see cref="UserRoleResourceDefinition"/> (ADR-0033): a
/// non-Admin's visible set here is never partially narrowed - every row is visible to an Admin or to
/// nobody - so <see cref="OnApplyFilter"/> rejects a non-Admin outright, collection or single alike,
/// rather than silently filtering to an empty collection.
/// </remarks>
public sealed class ApplicationUserResourceDefinition : JsonApiResourceDefinition<ApplicationUser, string>
{
    private const string NotAdminTitle = "You do not have permission to view Users.";

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Constructs the definition with the services it needs to authorize <see cref="ApplicationUser"/> reads.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/>.</param>
    public ApplicationUserResourceDefinition(IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every non-Admin request is rejected outright, collection or single alike - see this type's remarks
    /// for why that departs from <see cref="EventResourceDefinition.OnApplyFilter"/>'s silent-filter
    /// behavior.
    /// </remarks>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        var policy = new ApplicationUserAccessPolicy(
            _httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
                "ApplicationUserResourceDefinition requires an active HttpContext."));

        if (!policy.CanRead)
        {
            throw ForbiddenException(NotAdminTitle);
        }

        return existingFilter;
    }

    /// <remarks>Matches <see cref="UserRoleResourceDefinition"/>'s own private helper of the same name/shape.</remarks>
    private static JsonApiException ForbiddenException(string title) =>
        new(new ErrorObject(System.Net.HttpStatusCode.Forbidden) { Title = title });
}
