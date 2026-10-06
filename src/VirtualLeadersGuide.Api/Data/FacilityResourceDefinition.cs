using System.Net;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Serialization.Objects;
using Microsoft.EntityFrameworkCore;
using VirtualLeadersGuide.Api.Authorization;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Enforces Read-for-any-signed-in-caller, Write-Admin-only scoping on <c>/api/facilities</c> (P8-2, #166;
/// ADR-0070): an <c>Admin</c> or a <c>Director</c> (platform-wide or Event-scoped - a Facility has no owning
/// Event to scope against, ADR-0066) may read every Facility; only an <c>Admin</c> may create, update, or
/// delete one.
/// </summary>
/// <remarks>
/// Simpler than <see cref="ActivityResourceDefinition"/>'s shape: Read is binary
/// (<see cref="FacilityAccessPolicy.CanRead"/>), not per-row - there's no Event-assignment set to narrow a
/// collection against, so a non-Admin/non-Director's request is denied outright rather than silently
/// narrowed to an empty page. No <c>StoredEventId</c>/sentinel machinery either, since nothing here is
/// Event-scoped to re-read.
/// </remarks>
public sealed class FacilityResourceDefinition : JsonApiResourceDefinition<Facility, Guid>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;

    /// <summary>Constructs the definition with the services it needs to authorize Facility reads and writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> for <see cref="CurrentPolicy"/>.</param>
    /// <param name="dbContext">Backs <see cref="ValidateFacilityTypeExistsAsync"/>'s pre-check.</param>
    public FacilityResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unconditional 403 for a caller without <see cref="FacilityAccessPolicy.CanRead"/>, whether the
    /// request is a collection or a single resource - Read is all-or-nothing here (ADR-0070), unlike
    /// <see cref="EventResourceDefinition"/>/<see cref="ActivityResourceDefinition"/>'s per-Event narrowing.
    /// </remarks>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        if (!CurrentPolicy().CanRead)
        {
            throw ForbiddenException();
        }

        return existingFilter;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Authorizes first (<see cref="FacilityAccessPolicy.CanWrite"/>, Admin-only), then - on
    /// <see cref="WriteOperationKind.CreateResource"/> and <see cref="WriteOperationKind.UpdateResource"/> -
    /// pre-checks the target <see cref="Facility.FacilityTypeId"/> resolves to a real <see cref="FacilityType"/>
    /// row, the same shape <see cref="ActivityResourceDefinition.ValidateEventExistsAsync"/> uses for
    /// <see cref="Activity.EventId"/>.
    /// </remarks>
    public override async Task OnWritingAsync(
        Facility resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
    {
        if (!CurrentPolicy().CanWrite)
        {
            throw ForbiddenException();
        }

        if (writeOperation is WriteOperationKind.CreateResource or WriteOperationKind.UpdateResource)
        {
            await ValidateFacilityTypeExistsAsync(resource.FacilityTypeId, cancellationToken);
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
    }

    /// <remarks>
    /// Runs after authorization, so a non-Admin never learns whether an unknown Facility Type id exists -
    /// they get 403 either way, from the check above. This mainly protects the Admin path from an
    /// FK-violation 500.
    /// </remarks>
    private async Task ValidateFacilityTypeExistsAsync(Guid facilityTypeId, CancellationToken cancellationToken)
    {
        bool exists = await _dbContext.FacilityTypes.AsNoTracking()
            .AnyAsync(ft => ft.Id == facilityTypeId, cancellationToken);

        if (exists)
        {
            return;
        }

        throw new JsonApiException(new ErrorObject(HttpStatusCode.UnprocessableEntity)
        {
            Title = "Unknown Facility Type.",
            Detail = $"Facility Type '{facilityTypeId}' does not exist.",
            Source = new ErrorSource { Pointer = "/data/attributes/facilityTypeId" }
        });
    }

    private FacilityAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "FacilityResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException() =>
        JsonApiResourceDefinitionHelpers.ForbiddenException("You do not have permission to access this Facility.");
}
