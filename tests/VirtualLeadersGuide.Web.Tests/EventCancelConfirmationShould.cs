using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Mirrors <see cref="EventDeleteConfirmationShould"/>'s shape for <see cref="EventCancelConfirmation"/>
/// (P2-20, #115), including its conventions: no consequence bullet ever reads "0 directors" - a zero count
/// omits the bullet entirely - and a failed count degrades to explanatory text rather than blocking the
/// dialog (ADR-0045).
/// </remarks>
public class EventCancelConfirmationShould
{
    [Fact]
    public void SetTheMessageToCancelTheEventsName_WhenBuilt_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventCancelConfirmation.BuildDialogParameters("Summer Camporee 2026", 3);

        Assert.Equal("Cancel Summer Camporee 2026?", parameters[nameof(ConfirmDialog.Message)]);
    }

    [Fact]
    public void SetConfirmAndDismissText_Always_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventCancelConfirmation.BuildDialogParameters("Summer Camporee 2026", 3);

        Assert.Equal("Cancel event", parameters[nameof(ConfirmDialog.ConfirmText)]);
        Assert.Equal("Keep it live", parameters[nameof(ConfirmDialog.DismissText)]);
    }

    [Fact]
    public void IncludeThePluralDirectorsBullet_WhenTheCountIsGreaterThanOne_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventCancelConfirmation.BuildDialogParameters("Summer Camporee 2026", 3);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("3 assigned directors will see it as cancelled", consequences);
    }

    [Fact]
    public void IncludeTheSingularDirectorBullet_WhenTheCountIsOne_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventCancelConfirmation.BuildDialogParameters("Summer Camporee 2026", 1);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("1 assigned director will see it as cancelled", consequences);
    }

    [Fact]
    public void OmitTheDirectorsBullet_WhenTheCountIsZero_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventCancelConfirmation.BuildDialogParameters("Summer Camporee 2026", 0);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.DoesNotContain(consequences, c => c.Contains("director", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DegradeToAnExplanatoryBullet_WhenTheDirectorCountIsNull_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventCancelConfirmation.BuildDialogParameters("Summer Camporee 2026", directorCount: null);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("Directors with access couldn't be loaded", consequences);
    }

    [Fact]
    public void IncludeThePublicGuideAndIrreversibilityBullets_Always_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventCancelConfirmation.BuildDialogParameters("Summer Camporee 2026", 0);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("Its public Leaders Guide stops serving the guide", consequences);
        Assert.Contains("It drops off the default Events list", consequences);
        Assert.Contains("It can't be un-cancelled - Duplicate is the only way to start over", consequences);
    }
}
