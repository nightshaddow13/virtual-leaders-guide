using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// The Activity page's "Add a placement" builder (P5-11, #96; wireframe turns 1e/2b): a four-level autofill
/// chain - Tab required, then optionally Sub Tab, Section and Sub Section - a resolved-path preview with NEW
/// badges, a live duplicate guard, and a "Place it" button that hands the finished <see cref="PlacementPath"/>
/// to its host as an unsaved ghost row. Never talks to Api itself; the host owns the ghost list and Save.
/// </summary>
/// <remarks>
/// Every choice, canonical casing and duplicate answer comes from the host's <see cref="Model"/> - a
/// <see cref="PlacementTreeModel"/> already merged with this session's ghosts (plan decision 4) - so this
/// component holds only what the user has typed. The two chains skip independently (CONTEXT.md): a Section
/// needs only a Tab, not a Sub Tab; a Sub Section needs a Section. Changing a level clears the levels that
/// were scoped under its old value, since their choices no longer apply.
/// </remarks>
public partial class PlacementBuilder
{
    /// <summary>The merged server-plus-ghost tree the choices and duplicate check run against.</summary>
    [Parameter, EditorRequired]
    public PlacementTreeModel Model { get; set; } = default!;

    /// <summary>How many Placements this Activity has so far, saved and pending - the "2 SO FAR" counter.</summary>
    [Parameter]
    public int PlacedCount { get; set; }

    /// <summary>Raised with the canonicalized path when the user clicks "Place it".</summary>
    [Parameter]
    public EventCallback<PlacementPath> OnPlace { get; set; }

    private string? tab;
    private string? subTab;
    private string? section;
    private string? subSection;
    private PlacementPath? resolved;
    private IReadOnlyList<PathLevel> levels = [];
    private bool isDuplicate;

    private bool HasTab => !string.IsNullOrWhiteSpace(tab);

    private bool HasSection => HasTab && !string.IsNullOrWhiteSpace(section);

    /// <inheritdoc/>
    protected override void OnParametersSet() => Recompute();

    private void OnTabChanged(string? value)
    {
        tab = value;
        subTab = section = subSection = null;
        Recompute();
    }

    private void OnSubTabChanged(string? value)
    {
        subTab = value;
        section = subSection = null;
        Recompute();
    }

    private void OnSectionChanged(string? value)
    {
        section = value;
        subSection = null;
        Recompute();
    }

    private void OnSubSectionChanged(string? value)
    {
        subSection = value;
        Recompute();
    }

    /// <remarks>
    /// Re-derives the preview from the four typed values and the current <see cref="Model"/>. Runs on every
    /// parameter set as well as every keystroke because the host swaps in a fresh model after each "Place
    /// it" - a path that was valid a moment ago may now be a duplicate of the ghost just added.
    /// </remarks>
    private void Recompute()
    {
        if (!HasTab)
        {
            resolved = null;
            levels = [];
            isDuplicate = false;
            return;
        }

        resolved = Model.Canonicalize(new PlacementPath(tab!, subTab, section, subSection));
        levels = Model.Describe(resolved);
        isDuplicate = Model.IsAlreadyPlaced(resolved);
    }

    private async Task PlaceAsync()
    {
        if (resolved is null || isDuplicate)
        {
            return;
        }

        PlacementPath path = resolved;
        tab = subTab = section = subSection = null;
        Recompute();
        await OnPlace.InvokeAsync(path);
    }
}
