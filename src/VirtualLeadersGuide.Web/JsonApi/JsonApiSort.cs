using Radzen;

namespace VirtualLeadersGuide.Web.JsonApi;

/// <summary>
/// Maps a <see cref="Radzen.Blazor.RadzenDataGrid{TItem}"/>'s <see cref="SortDescriptor"/> onto JSON:API's
/// <c>sort=</c>/<c>sort=-</c> query syntax - shared by every grid whose user-sortable column needs to become
/// an Api-side sort parameter. Extracted out of <c>Dashboard.razor.cs</c> (its first caller) once a second
/// caller (<c>ActivityList.razor.cs</c>, P5-7 #93) needed the identical mapping (P5-17, #22 code review's
/// precedent for consolidating near-identical copies - see <see cref="VirtualLeadersGuide.Web.JsonApi"/>'s
/// own <c>JsonApiEnvelope.cs</c>).
/// </summary>
internal static class JsonApiSort
{
    /// <summary>Maps the grid's first sort descriptor onto a JSON:API <c>sort=</c> value.</summary>
    /// <param name="sorts">The grid's current sort descriptors, from <c>LoadDataArgs.Sorts</c>.</param>
    /// <returns>
    /// <see langword="null"/> when nothing is sorted; otherwise the (possibly <c>-</c>-prefixed) property
    /// name.
    /// </returns>
    /// <remarks>
    /// Lowercases only the first character, not the whole property name - JsonApiDotNetCore's default
    /// naming exposes an attribute in camelCase (<c>startsAt</c>, not <c>startsat</c>); lowering the whole
    /// string was harmless while every sortable column's name was one word (<c>name</c>, <c>slug</c>) but
    /// would 400 a sort on <c>EventDto.StartsAt</c>.
    /// </remarks>
    public static string? ToJsonApiSort(IEnumerable<SortDescriptor>? sorts)
    {
        SortDescriptor? first = sorts?.FirstOrDefault();
        if (first?.Property is not { Length: > 0 } property)
        {
            return null;
        }

        property = char.ToLowerInvariant(property[0]) + property[1..];
        return first.SortOrder == SortOrder.Descending ? $"-{property}" : property;
    }
}
