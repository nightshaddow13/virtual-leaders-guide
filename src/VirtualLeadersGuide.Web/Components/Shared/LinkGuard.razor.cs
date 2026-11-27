using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// While <see cref="Active"/>, turns a click on any same-origin link into navigation the circuit performs, so
/// the page's location-changing handler gets to ask first (P5-23, #206).
/// </summary>
/// <remarks>
/// The router isn't interactive here (pages opt into a render mode one at a time), so Blazor's enhanced
/// navigation follows a link entirely in the browser: no location-changing handler runs, and nothing unloads
/// for a <c>NavigationLock</c> to catch. The browser's Back button isn't a click and isn't covered.
/// </remarks>
public partial class LinkGuard
{
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>Whether link clicks should be routed through the circuit.</summary>
    [Parameter]
    public bool Active { get; set; }

    private ElementReference marker;
    private IJSObjectReference? module;
    private IJSObjectReference? watch;
    private DotNetObjectReference<LinkGuard>? self;

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        self = DotNetObjectReference.Create(this);
        try
        {
            module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./Components/Shared/LinkGuard.razor.js");
            watch = await module.InvokeAsync<IJSObjectReference>("watch", marker, self);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>Called by the script in place of the browser following a link.</summary>
    /// <param name="href">The link's absolute URL.</param>
    [JSInvokable]
    public void Follow(string href) => NavigationManager.NavigateTo(href);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (watch is not null)
            {
                await watch.InvokeVoidAsync("dispose");
                await watch.DisposeAsync();
            }

            if (module is not null)
            {
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        self?.Dispose();
    }
}
