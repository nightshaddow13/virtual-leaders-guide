using Microsoft.AspNetCore.Components;
using Radzen;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// A single reusable confirm dialog for every destructive action in this app (ADR-0045) - opened via
/// <see cref="DialogService.OpenAsync{T}(string, System.Collections.Generic.Dictionary{string, object}?, DialogOptions?)"/>
/// the same way <c>Users.razor.cs</c> already opens <c>InviteDirectorDialog</c>, never a bespoke one-off dialog.
/// </summary>
/// <remarks>
/// Confirms only - it never performs the action itself. The caller owns the destructive call and any error
/// handling once <see cref="DialogService.Close(object?)"/> returns <see langword="true"/>; <see langword="false"/>
/// means Cancel was chosen and nothing should happen.
/// </remarks>
public partial class ConfirmDialog
{
    [Inject]
    private DialogService DialogService { get; set; } = default!;

    /// <summary>The question line shown above the consequence list, e.g. "Delete Summer Camporee 2026?".</summary>
    [Parameter, EditorRequired]
    public string Message { get; set; } = "";

    /// <summary>
    /// The consequences of confirming, one bullet each. A caller omits an individual consequence from this list
    /// rather than passing a string for one that doesn't apply (ADR-0045) - e.g. an Event with no Directors
    /// assigned carries no "Directors lose access" bullet at all. <see langword="null"/> or empty renders no
    /// list.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string>? Consequences { get; set; }

    /// <summary>Text on the confirm button. Defaults to "Delete" - this app's first, and so far only, use.</summary>
    [Parameter]
    public string ConfirmText { get; set; } = "Delete";

    /// <summary>
    /// Text on the dismiss (left) button. Defaults to "Cancel", which reads fine for every delete
    /// confirmation - a caller whose own confirm action is itself named "Cancel" (P2-20, #115's "Cancel
    /// event") overrides this, since "Cancel" / "Cancel event" side by side is genuinely ambiguous about
    /// which button does what.
    /// </summary>
    [Parameter]
    public string DismissText { get; set; } = "Cancel";

    /// <summary>Style of the confirm button. Defaults to <see cref="ButtonStyle.Danger"/>.</summary>
    [Parameter]
    public ButtonStyle ConfirmButtonStyle { get; set; } = ButtonStyle.Danger;
}
