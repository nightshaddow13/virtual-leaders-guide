using Microsoft.AspNetCore.Components;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// A search box that reports its text through <see cref="SearchChanged"/> once the user pauses typing, with a
/// button to clear it (P2.1-8, #205).
/// </summary>
public sealed partial class SearchBox : IDisposable
{
    private string text = "";
    private CancellationTokenSource? pending;

    /// <summary>The hint shown while the box is empty.</summary>
    [Parameter]
    public string? Placeholder { get; set; }

    /// <summary>The box's accessible name.</summary>
    [Parameter, EditorRequired]
    public string AriaLabel { get; set; } = default!;

    /// <summary>Raised with the trimmed search text, empty when there is nothing to search for.</summary>
    [Parameter]
    public EventCallback<string> SearchChanged { get; set; }

    /// <summary>How long typing has to pause before <see cref="SearchChanged"/> is raised.</summary>
    [Parameter]
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <inheritdoc/>
    public void Dispose() => CancelPending();

    /// <remarks>
    /// Each keystroke abandons the previous one's wait, so only the text the user stopped on is reported.
    /// This spares a request per keystroke; it doesn't order the responses, which is the host's job.
    /// </remarks>
    private async Task OnInputAsync(string? value)
    {
        text = value ?? "";
        CancelPending();
        var current = pending = new CancellationTokenSource();

        try
        {
            await Task.Delay(DebounceDelay, current.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SearchChanged.InvokeAsync(text.Trim());
    }

    /// <remarks>A click is a finished intent, so it skips the debounce.</remarks>
    private async Task ClearAsync()
    {
        CancelPending();
        text = "";
        await SearchChanged.InvokeAsync(text);
    }

    private void CancelPending()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
    }
}
