using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// A pure function, not a rendered component - covers the actual Message/Consequences content
/// <c>Dashboard.razor.cs</c> and <c>EventEditor.razor.cs</c> hand to <c>ConfirmDialog</c> for a given
/// Event/Director-count combination, which <see cref="ConfirmDialogShould"/> doesn't (it only exercises the
/// dialog's own rendering/close behavior against hand-picked values).
/// </remarks>
public class EventDeleteConfirmationShould
{
    [Fact]
    public void SetTheMessageToDeleteTheEventsName_WhenBuilt_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventDeleteConfirmation.BuildDialogParameters("Summer Camporee 2026", "summer-camporee-2026", 3);

        Assert.Equal("Delete Summer Camporee 2026?", parameters[nameof(ConfirmDialog.Message)]);
    }

    [Fact]
    public void IncludeThePluralDirectorsBullet_WhenTheCountIsGreaterThanOne_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventDeleteConfirmation.BuildDialogParameters("Summer Camporee 2026", "summer-camporee-2026", 3);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("3 directors lose access to this event", consequences);
    }

    [Fact]
    public void IncludeTheSingularDirectorBullet_WhenTheCountIsOne_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventDeleteConfirmation.BuildDialogParameters("Summer Camporee 2026", "summer-camporee-2026", 1);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("1 director loses access to this event", consequences);
    }

    /// <remarks>Grilled decision (P2-17): no consequence should read "0 directors" - the bullet is omitted entirely instead.</remarks>
    [Fact]
    public void OmitTheDirectorsBullet_WhenTheCountIsZero_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventDeleteConfirmation.BuildDialogParameters("Summer Camporee 2026", "summer-camporee-2026", 0);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.DoesNotContain(consequences, c => c.Contains("director", StringComparison.OrdinalIgnoreCase));
    }

    /// <remarks>Grilled decision (P2-17), pinned in ADR-0045: a failed count degrades to explanatory text rather than blocking the dialog.</remarks>
    [Fact]
    public void DegradeToAnExplanatoryBullet_WhenTheDirectorCountIsNull_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventDeleteConfirmation.BuildDialogParameters("Summer Camporee 2026", "summer-camporee-2026", directorCount: null);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("Directors with access couldn't be loaded", consequences);
    }

    [Fact]
    public void IncludeTheSlugAndIrreversibilityBullets_Always_ForBuildDialogParameters()
    {
        Dictionary<string, object?> parameters =
            EventDeleteConfirmation.BuildDialogParameters("Summer Camporee 2026", "summer-camporee-2026", 0);

        var consequences = (IReadOnlyList<string>)parameters[nameof(ConfirmDialog.Consequences)]!;
        Assert.Contains("The address /e/summer-camporee-2026 frees up for reuse", consequences);
        Assert.Contains("This can't be undone", consequences);
    }
}
