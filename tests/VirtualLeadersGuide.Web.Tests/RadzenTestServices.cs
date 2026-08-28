using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Any page whose markup includes <c>&lt;RadzenComponents&gt;</c> - the host for <c>RadzenDialog</c>,
/// <c>RadzenNotification</c>, <c>RadzenContextMenu</c>, and <c>RadzenTooltip</c> - needs all four of their
/// backing services registered to render under bUnit, regardless of which ones the page's own code-behind
/// actually injects and calls.
/// </remarks>
internal static class RadzenTestServices
{
    public static void RegisterRadzenComponentsHost(IServiceCollection services)
    {
        services.AddSingleton<NotificationService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<ContextMenuService>();
        services.AddSingleton<TooltipService>();
    }
}
