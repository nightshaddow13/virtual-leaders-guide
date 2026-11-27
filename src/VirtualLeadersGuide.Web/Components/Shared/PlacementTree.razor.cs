using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// The Activity page's "Where it appears" tree (P5-10/P5-11; wireframe turn 2a) with a remove control on this
/// Activity's own rows - saved and pending alike (P5-12, #97) - and ↑/↓ on its saved rows (P5-13, #98). Owns
/// no behavior of its own: it raises <see cref="OnRemove"/> and <see cref="OnMove"/> and the host decides what
/// they mean.
/// </summary>
public partial class PlacementTree
{
    /// <summary>The tree's top level - every Tab - from <see cref="PlacementTreeModel.Roots"/>.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<TierNode> Roots { get; set; } = [];

    /// <summary>Raised with the row whose × was clicked. Only this Activity's rows carry one.</summary>
    [Parameter]
    public EventCallback<PlacementLeaf> OnRemove { get; set; }

    /// <summary>Raised when an arrow is clicked. Only this Activity's saved rows carry them.</summary>
    [Parameter]
    public EventCallback<PlacementMove> OnMove { get; set; }

    /// <summary>Whether a save, removal or move is in flight - disables every control so two can't overlap.</summary>
    [Parameter]
    public bool IsBusy { get; set; }

    /// <summary>Which row's arrow should take focus after a move.</summary>
    [Parameter]
    public PlacementFocusRequest? FocusRequest { get; set; }

    /// <summary>Screen-reader text saying where a moved row landed.</summary>
    [Parameter]
    public string? Announcement { get; set; }
}
