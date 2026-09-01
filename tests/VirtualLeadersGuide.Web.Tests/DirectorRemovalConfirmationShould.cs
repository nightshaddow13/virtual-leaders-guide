using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Directors;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// A pure function, not a rendered component - same reasoning as <c>EventDeleteConfirmationShould</c>:
/// covers the actual Message/Consequences/ConfirmText content <c>EventEditor.razor.cs</c> hands to
/// <c>ConfirmDialog</c> for a given Director/Event pair, which <c>ConfirmDialogShould</c> doesn't (it only
/// exercises the dialog's own rendering/close behavior against hand-picked values).
/// </remarks>
public class DirectorRemovalConfirmationShould
{
    [Fact]
    public void SetTheMessageToRemoveTheDirectorFromTheEvent_WhenBuilt_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            DirectorRemovalConfirmation.BuildDialogParameters("Pat Riley", "Summer Camporee 2026");

        Assert.Equal("Remove Pat Riley from Summer Camporee 2026?", parameters[nameof(ConfirmDialog.Message)]);
    }

    [Fact]
    public void IncludeTheRoleIsKeptBullet_Always_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            DirectorRemovalConfirmation.BuildDialogParameters("Pat Riley", "Summer Camporee 2026");

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("They keep the Director role and any other events they hold", consequences);
    }

    [Fact]
    public void IncludeTheAccessLostAndAddBackBullets_Always_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            DirectorRemovalConfirmation.BuildDialogParameters("Pat Riley", "Summer Camporee 2026");

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("They lose access to this event", consequences);
        Assert.Contains("You can add them back from this page at any time", consequences);
    }

    [Fact]
    public void SetTheConfirmTextToRemove_Always_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            DirectorRemovalConfirmation.BuildDialogParameters("Pat Riley", "Summer Camporee 2026");

        Assert.Equal("Remove", parameters[nameof(ConfirmDialog.ConfirmText)]);
    }
}
