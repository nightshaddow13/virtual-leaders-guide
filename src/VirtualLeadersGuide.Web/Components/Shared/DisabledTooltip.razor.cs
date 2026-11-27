using Microsoft.AspNetCore.Components;
using Radzen;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// Explains why the control inside it is disabled: a tooltip on hover or keyboard focus, and the same text as the
/// wrapper's accessible name.
/// </summary>
/// <remarks>See ADR-0052 for why a wrapper element owns the triggers rather than the disabled control.</remarks>
public partial class DisabledTooltip
{
    private readonly string reasonId = $"disabled-reason-{Guid.NewGuid():n}";
    private ElementReference anchor;

    [Inject]
    private TooltipService TooltipService { get; set; } = default!;

    /// <summary>Why the control is disabled.</summary>
    [Parameter, EditorRequired]
    public string Text { get; set; } = "";

    /// <summary>The disabled control.</summary>
    [Parameter, EditorRequired]
    public RenderFragment ChildContent { get; set; } = default!;

    /// <summary>Extra attributes for the wrapper element, such as <c>style</c>.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private void Show() => TooltipService.Open(anchor, Text);

    private void Hide() => TooltipService.Close();
}
