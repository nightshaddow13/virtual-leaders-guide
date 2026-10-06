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
/// Enforces Admin-only scoping on <c>/api/facilityTypes</c> (P8-2, #166): every verb, Read included, requires
/// <see cref="FacilityAccessPolicy.IsAdmin"/> - the only consumer is the Admin-only Facility create/edit
/// form, so unlike <see cref="FacilityResourceDefinition"/> there's no broader Read audience to open up.
/// </summary>
/// <remarks>
/// Much simpler than <see cref="InfoPageResourceDefinition"/>'s shape - no Event-scoping, no per-row
/// filter-narrowing, just a flat gate: a non-Admin's collection request is denied outright (403), not
/// silently narrowed to an empty page, since this resource has nothing to narrow *to*.
/// </remarks>
public sealed class FacilityTypeResourceDefinition : JsonApiResourceDefinition<FacilityType, Guid>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly VirtualLeadersGuideDbContext _dbContext;

    /// <summary>Constructs the definition with the services it needs to authorize Facility Type reads and writes.</summary>
    /// <param name="resourceGraph">Passed through to <see cref="JsonApiResourceDefinition{TResource,TId}"/>.</param>
    /// <param name="httpContextAccessor">Resolves the current request's <see cref="System.Security.Claims.ClaimsPrincipal"/> for <see cref="CurrentPolicy"/>.</param>
    /// <param name="dbContext">Backs <see cref="CheckForConflictsAsync"/>'s Name uniqueness pre-check.</param>
    public FacilityTypeResourceDefinition(
        IResourceGraph resourceGraph, IHttpContextAccessor httpContextAccessor, VirtualLeadersGuideDbContext dbContext)
        : base(resourceGraph)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unconditional 403 for a non-Admin, whether the request is a collection or a single resource - there's
    /// no partial visibility to narrow a collection down to, unlike <see cref="EventResourceDefinition"/>'s
    /// Director-scoped narrowing.
    /// </remarks>
    public override FilterExpression? OnApplyFilter(FilterExpression? existingFilter)
    {
        if (!CurrentPolicy().IsAdmin)
        {
            throw ForbiddenException();
        }

        return existingFilter;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Authorizes first, then - <see cref="WriteOperationKind.CreateResource"/> and
    /// <see cref="WriteOperationKind.UpdateResource"/> only - pre-checks <see cref="FacilityType.Name"/>
    /// isn't already taken by another row, the same case-insensitive pre-check pattern
    /// <see cref="EventResourceDefinition.CheckForConflictsAsync"/> uses for <see cref="Event.Slug"/>. This
    /// is what keeps the Web client's resolve-or-create autofill flow from racing itself into two
    /// near-duplicate rows, though a true concurrent double-submit still isn't fully closed - same accepted
    /// gap <see cref="EventResourceDefinition"/>'s own remarks describe for <see cref="Event.Name"/>.
    /// </remarks>
    public override async Task OnWritingAsync(
        FacilityType resource, WriteOperationKind writeOperation, CancellationToken cancellationToken)
    {
        if (!CurrentPolicy().IsAdmin)
        {
            throw ForbiddenException();
        }

        if (writeOperation is WriteOperationKind.CreateResource or WriteOperationKind.UpdateResource)
        {
            await CheckForConflictsAsync(resource, cancellationToken);
        }

        await base.OnWritingAsync(resource, writeOperation, cancellationToken);
    }

    /// <remarks>
    /// A pre-check, not a <see cref="DbUpdateException"/> catch - see <see cref="EventResourceDefinition.CheckForConflictsAsync"/>'s
    /// remarks for why a portable pre-check is preferred over catching the provider-specific unique-index
    /// violation. Compares case-insensitively to match SQL Server's default collation and
    /// <c>IX_FacilityTypes_Name</c>'s intent.
    /// </remarks>
    private async Task CheckForConflictsAsync(FacilityType resource, CancellationToken cancellationToken)
    {
        string normalizedName = resource.Name.ToUpperInvariant();

        bool nameTaken = await _dbContext.FacilityTypes.AsNoTracking()
            .AnyAsync(ft => ft.Id != resource.Id && ft.Name.ToUpper() == normalizedName, cancellationToken);

        if (!nameTaken)
        {
            return;
        }

        throw new JsonApiException(new ErrorObject(HttpStatusCode.Conflict)
        {
            Title = "Resource conflict.",
            Detail = $"Facility Type '{resource.Name}' already exists.",
            Source = new ErrorSource { Pointer = "/data/attributes/name" }
        });
    }

    private FacilityAccessPolicy CurrentPolicy() =>
        new(_httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException(
            "FacilityTypeResourceDefinition requires an active HttpContext."));

    private static JsonApiException ForbiddenException() =>
        JsonApiResourceDefinitionHelpers.ForbiddenException("You do not have permission to access this Facility Type.");
}
