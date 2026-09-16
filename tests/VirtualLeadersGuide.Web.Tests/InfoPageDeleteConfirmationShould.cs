using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.InfoPages;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="EventDeleteConfirmationShould"/>'s shape - a pure function, not a rendered component.
/// </remarks>
public class InfoPageDeleteConfirmationShould
{
    [Fact]
    public void SetTheMessageToDeleteTheInfoPagesTitle_WhenBuilt_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = InfoPageDeleteConfirmation.BuildDialogParameters("Packing List");

        Assert.Equal("Delete Packing List?", parameters[nameof(ConfirmDialog.Message)]);
    }

    /// <remarks>Grilled decision (P5-17, #22): no Placement-related bullet - P5-18/19/20 haven't shipped, so nothing is placed yet to warn about losing.</remarks>
    [Fact]
    public void IncludeOnlyTheIrreversibilityBullet_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = InfoPageDeleteConfirmation.BuildDialogParameters("Packing List");

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Equal(["This can't be undone"], consequences);
    }
}
