using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// The Activity page's read-only "Where it appears" tree (P5-10/P5-11; wireframe turn 2a) with a remove
/// control on this Activity's own rows - saved and pending alike (P5-12, #97). Owns no behavior of its own:
/// it raises <see cref="OnRemove"/> and the host decides what removing means for a saved row versus a ghost.
/// </summary>
public partial class PlacementTree
{
    /// <summary>The tree's top level - every Tab - from <see cref="PlacementTreeModel.Roots"/>.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<TierNode> Roots { get; set; } = [];

    /// <summary>Raised with the row whose × was clicked. Only this Activity's rows carry one.</summary>
    [Parameter]
    public EventCallback<PlacementLeaf> OnRemove { get; set; }

    /// <summary>Whether a save or removal is in flight - disables every × so two can't overlap.</summary>
    [Parameter]
    public bool IsBusy { get; set; }
}
