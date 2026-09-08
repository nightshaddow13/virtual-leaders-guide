using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Directors;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// A pure function, not a rendered component - same reasoning as <c>EventDeleteConfirmationShould</c> and
/// <c>DirectorRemovalConfirmationShould</c>.
/// </remarks>
public class UserDeleteConfirmationShould
{
    [Fact]
    public void SetTheMessageToDeleteTheUser_WhenBuilt_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = UserDeleteConfirmation.BuildDialogParameters("Pat Riley", 0);

        Assert.Equal("Delete Pat Riley?", parameters[nameof(ConfirmDialog.Message)]);
    }

    [Fact]
    public void OmitTheEventsBullet_WhenTheEventGrantCountIsZero_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = UserDeleteConfirmation.BuildDialogParameters("Pat Riley", 0);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.DoesNotContain(consequences, c => c.Contains("event", StringComparison.OrdinalIgnoreCase) && c.Contains("lose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IncludeASingularEventsBullet_WhenTheEventGrantCountIsOne_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = UserDeleteConfirmation.BuildDialogParameters("Pat Riley", 1);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("They lose access to 1 event", consequences);
    }

    [Fact]
    public void IncludeAPluralEventsBullet_WhenTheEventGrantCountIsMoreThanOne_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = UserDeleteConfirmation.BuildDialogParameters("Pat Riley", 3);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("They lose access to 3 events", consequences);
    }

    [Fact]
    public void IncludeTheRoleAndUndoBullets_Always_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters = UserDeleteConfirmation.BuildDialogParameters("Pat Riley", 0);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("Their Director role and every event grant are removed", consequences);
        Assert.Contains("This can't be undone", consequences);
    }
}
