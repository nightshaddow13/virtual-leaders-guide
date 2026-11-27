using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Facilities;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="InfoPageDeleteConfirmationShould"/>'s shape - a pure function, not a rendered component.
/// The irreversibility bullet is the only one until something can exist under a Facility (ADR-0081).
/// </remarks>
public class FacilityDeleteConfirmationShould
{
    [Fact]
    public void SetTheMessageToDeleteTheFacilitysName_WhenBuilt_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = FacilityDeleteConfirmation.BuildDialogParameters("Camp Blackhawk");

        Assert.Equal("Delete Camp Blackhawk?", parameters[nameof(ConfirmDialog.Message)]);
    }

    [Fact]
    public void IncludeOnlyTheIrreversibilityBullet_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = FacilityDeleteConfirmation.BuildDialogParameters("Camp Blackhawk");

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Equal(["This can't be undone"], consequences);
    }
}
