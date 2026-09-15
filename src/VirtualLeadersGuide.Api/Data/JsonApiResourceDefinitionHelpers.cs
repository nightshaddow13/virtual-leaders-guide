using System.Net;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Serialization.Objects;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Small pieces of <c>JsonApiResourceDefinition</c> plumbing shared across this Api's resource definitions -
/// <see cref="EventResourceDefinition"/>, <see cref="InfoPageResourceDefinition"/>, and (for
/// <see cref="ForbiddenException"/> only) <see cref="UserRoleResourceDefinition"/>.
/// </summary>
/// <remarks>
/// Each resource definition still owns its own <c>CurrentPolicy()</c> one-liner rather than sharing it here -
/// the policy type differs per resource (<see cref="Authorization.EventAccessPolicy"/>,
/// <see cref="Authorization.InfoPageAccessPolicy"/>, <see cref="Authorization.RoleGrantAccessPolicy"/>), and a
/// generic parameter to share three lines with no other shared behavior isn't worth the indirection.
/// </remarks>
internal static class JsonApiResourceDefinitionHelpers
{
    /// <summary>ANDs <paramref name="right"/> onto <paramref name="left"/>, or returns <paramref name="right"/> alone when there's nothing yet to AND it with.</summary>
    public static FilterExpression? And(FilterExpression? left, FilterExpression right) =>
        left is null ? right : new LogicalExpression(LogicalOperator.And, left, right);

    /// <summary>Resolves the current request's <see cref="IJsonApiRequest"/> - for a resource definition's <c>OnApplyFilter</c>.</summary>
    /// <param name="httpContextAccessor">The calling resource definition's own accessor.</param>
    /// <param name="requiringTypeName">The calling resource definition's type name, named in the exception thrown when there's no active request.</param>
    public static IJsonApiRequest GetRequest(IHttpContextAccessor httpContextAccessor, string requiringTypeName) =>
        httpContextAccessor.HttpContext?.RequestServices.GetRequiredService<IJsonApiRequest>()
            ?? throw new InvalidOperationException($"{requiringTypeName} requires an active HttpContext.");

    /// <summary>A 403 <see cref="JsonApiException"/> carrying <paramref name="title"/>.</summary>
    public static JsonApiException ForbiddenException(string title) =>
        new(new ErrorObject(HttpStatusCode.Forbidden) { Title = title });
}
