using Microsoft.AspNetCore.Components;
using Radzen.Blazor;
using VirtualLeadersGuide.Web.Activities;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// The ↑/↓ pair on one of this Activity's saved rows (P5-13, #98). Raises <see cref="OnMove"/> and, after the
/// host has redrawn the move, takes focus back when <see cref="FocusRequest"/> names this row.
/// </summary>
public partial class PlacementReorderButtons
{
    private RadzenButton? upButton;
    private RadzenButton? downButton;
    private PlacementFocusRequest? handledRequest;

    /// <summary>The tree row these arrows move.</summary>
    [Parameter, EditorRequired]
    public PlacementLeaf Leaf { get; set; } = default!;

    /// <summary>Raised with the requested step when an arrow is clicked.</summary>
    [Parameter]
    public EventCallback<PlacementMove> OnMove { get; set; }

    /// <summary>Whether the arrows can't be used right now.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>Which row's arrow should be focused after a move, or <see langword="null"/>.</summary>
    [Parameter]
    public PlacementFocusRequest? FocusRequest { get; set; }

    /// <summary>Raised once this row has honoured <see cref="FocusRequest"/>, so the host can drop it.</summary>
    [Parameter]
    public EventCallback OnFocused { get; set; }

    /// <remarks>
    /// Honours each request once, by reference: moving the same row the same way twice makes two equal
    /// records. Waits out <see cref="Disabled"/>, since a disabled button can't take focus, and falls back to
    /// the other arrow when the row has reached the top or bottom.
    /// </remarks>
    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Disabled || FocusRequest is not { } request || ReferenceEquals(request, handledRequest)
            || request.PlacementId != Leaf.PlacementId)
        {
            return;
        }

        handledRequest = request;
        bool up = request.Direction == PlacementDirection.Up ? Leaf.CanMoveUp : !Leaf.CanMoveDown;
        if ((up ? upButton : downButton) is { } button)
        {
            await button.Element.FocusAsync();
            await OnFocused.InvokeAsync();
        }
    }
}
