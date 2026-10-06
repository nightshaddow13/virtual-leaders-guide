using Radzen;
using VirtualLeadersGuide.Web.JsonApi;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Covers <see cref="JsonApiSort"/>, extracted out of <c>Dashboard.razor.cs</c> once <c>ActivityList.razor.cs</c>
/// (P5-7, #93) needed the identical mapping. Pins the behavior its own <c>&lt;remarks&gt;</c> warns about -
/// lowercasing only the first character, not the whole property name, so a camelCase attribute doesn't 400.
/// </remarks>
public class JsonApiSortShould
{
    [Fact]
    public void ReturnNull_WhenNoSortsAreGiven_ForToJsonApiSort()
    {
        string? sort = JsonApiSort.ToJsonApiSort(null);

        Assert.Null(sort);
    }

    [Fact]
    public void ReturnNull_WhenSortsIsEmpty_ForToJsonApiSort()
    {
        string? sort = JsonApiSort.ToJsonApiSort([]);

        Assert.Null(sort);
    }

    [Fact]
    public void ReturnTheBareProperty_WhenSortingAscending_ForToJsonApiSort()
    {
        var descriptor = new SortDescriptor { Property = "name", SortOrder = SortOrder.Ascending };

        string? sort = JsonApiSort.ToJsonApiSort([descriptor]);

        Assert.Equal("name", sort);
    }

    [Fact]
    public void ReturnADashPrefixedProperty_WhenSortingDescending_ForToJsonApiSort()
    {
        var descriptor = new SortDescriptor { Property = "name", SortOrder = SortOrder.Descending };

        string? sort = JsonApiSort.ToJsonApiSort([descriptor]);

        Assert.Equal("-name", sort);
    }

    /// <remarks>The case that would 400 against the API if the whole property name were lowercased instead of just its first character.</remarks>
    [Fact]
    public void LowercaseOnlyTheFirstCharacter_WhenThePropertyIsCamelCase_ForToJsonApiSort()
    {
        var descriptor = new SortDescriptor { Property = "StartsAt", SortOrder = SortOrder.Ascending };

        string? sort = JsonApiSort.ToJsonApiSort([descriptor]);

        Assert.Equal("startsAt", sort);
    }

    [Fact]
    public void UseOnlyTheFirstDescriptor_WhenMultipleAreGiven_ForToJsonApiSort()
    {
        var first = new SortDescriptor { Property = "name", SortOrder = SortOrder.Ascending };
        var second = new SortDescriptor { Property = "description", SortOrder = SortOrder.Descending };

        string? sort = JsonApiSort.ToJsonApiSort([first, second]);

        Assert.Equal("name", sort);
    }
}
