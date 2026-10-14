using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// One level of the Placement builder's autofill chain (P5-11, #96; wireframe turn 1e): existing choices with
/// usage counts, a "Create ‹name›" line when the typed text matches none, and a "did you mean" nudge when it
/// matches one only by case. The same component serves all four levels - Tab, Sub Tab, Section, Sub Section.
/// </summary>
public partial class PlacementTierField
{
    /// <summary>The visible label ("Tab", "Sub Tab", ...).</summary>
    [Parameter, EditorRequired]
    public string Label { get; set; } = "";

    /// <summary>The input's <c>name</c>/<c>id</c> - what the label points at and what tests drive.</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = "";

    /// <summary>The lowercase noun used in the create line ("tab", "sub tab", ...).</summary>
    [Parameter, EditorRequired]
    public string Noun { get; set; } = "";

    /// <summary>Placeholder shown while the input is empty.</summary>
    [Parameter]
    public string Placeholder { get; set; } = "Search or type a name";

    /// <summary>Whether this level must be filled - marks the label with an asterisk instead of "optional".</summary>
    [Parameter]
    public bool Required { get; set; }

    /// <summary>Whether the level is usable yet - a level stays locked until the level it depends on is filled.</summary>
    [Parameter]
    public bool Enabled { get; set; } = true;

    /// <summary>The text shown in place of the input while the level is locked ("pick a tab first").</summary>
    [Parameter]
    public string LockedHint { get; set; } = "";

    /// <summary>The typed or chosen text.</summary>
    [Parameter]
    public string? Value { get; set; }

    /// <summary>Raised on every change to <see cref="Value"/>.</summary>
    [Parameter]
    public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>The existing choices at this level, already scoped to the level above.</summary>
    [Parameter]
    public IReadOnlyList<TierOption> Options { get; set; } = [];

    private string? NearDuplicate => PlacementTreeModel.NearDuplicateOf(Options, Value);

    /// <remarks>
    /// Typed text that matches no existing choice (ignoring case) - saving would create it. Shown as a plain
    /// line, never a selectable row, so Enter can only ever take an existing match (wireframe 1e-3: "the
    /// create row is always last, never preselected"). Adapted from the wireframe's in-popup row because
    /// <c>RadzenAutoComplete</c> has no slot for a non-item row.
    /// </remarks>
    private bool IsCreating =>
        !string.IsNullOrWhiteSpace(Value) && !Options.Any(o => PlacementPath.SameName(o.Name, Value.Trim()));

    private string CountText(string optionName)
    {
        int count = Options.FirstOrDefault(o => o.Name == optionName)?.ActivityCount ?? 0;
        return count == 1 ? "1 activity" : $"{count} activities";
    }
}
