using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Rendered in isolation, the same way <c>InviteDirectorDialogShould</c> renders <c>InviteDirectorDialog</c> -
/// not through <c>DialogService.OpenAsync</c>, so <c>DialogService.OnClose</c> is subscribed directly to
/// observe what <see cref="ConfirmDialog"/>'s buttons pass to <c>DialogService.Close</c>. Confirmed against
/// Radzen.Blazor 11.2.6 by reflection: <c>Close</c> only raises <c>OnClose</c> once something has been pushed
/// onto the service's own internal open-dialog tracking - calling it with no prior <c>Open</c>/<c>OpenAsync</c>
/// is silently a no-op - so the two Click-driven tests below call the untyped, synchronous <c>Open</c> first to
/// put the service into that state, without needing a real rendered dialog host.
/// </remarks>
public class ConfirmDialogShould : BunitContext
{
    /// <remarks>See <see cref="DashboardRenderingShould"/>'s constructor remarks.</remarks>
    public ConfirmDialogShould() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void RenderTheMessage_WhenRendered_ForConfirmDialog()
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);

        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(parameters => parameters
            .Add(component => component.Message, "Delete Summer Camporee 2026?"));

        Assert.Contains("Delete Summer Camporee 2026?", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderEachConsequenceAsABullet_WhenConsequencesProvided_ForConfirmDialog()
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);

        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(parameters => parameters
            .Add(component => component.Message, "Delete Summer Camporee 2026?")
            .Add(component => component.Consequences,
            [
                "3 directors lose access to this event",
                "The address /e/summer-camporee-2026 frees up for reuse",
                "This can't be undone"
            ]));

        IReadOnlyList<IElement> bullets = cut.FindAll("li");
        Assert.Equal(3, bullets.Count);
        Assert.Contains(bullets, li => li.TextContent.Contains("3 directors lose access", StringComparison.Ordinal));
        Assert.Contains(bullets, li => li.TextContent.Contains("frees up for reuse", StringComparison.Ordinal));
        Assert.Contains(bullets, li => li.TextContent.Contains("can't be undone", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderNoBulletList_WhenConsequencesIsNullOrEmpty_ForConfirmDialog()
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);

        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(parameters => parameters
            .Add(component => component.Message, "Delete Fall Webelos Woods?"));

        Assert.Empty(cut.FindAll("li"));
    }

    [Fact]
    public void CloseWithFalse_WhenCancelIsClicked_ForConfirmDialog()
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
        DialogService dialogService = Services.GetRequiredService<DialogService>();
        bool? result = null;
        dialogService.OnClose += value => result = (bool)value;
        OpenTrackedDialog(dialogService);

        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(parameters => parameters
            .Add(component => component.Message, "Delete Summer Camporee 2026?"));
        IElement cancelButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Cancel", StringComparison.Ordinal));
        cancelButton.Click();

        Assert.Equal(false, result);
    }

    [Fact]
    public void CloseWithTrue_WhenTheDefaultDeleteConfirmIsClicked_ForConfirmDialog()
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);
        DialogService dialogService = Services.GetRequiredService<DialogService>();
        bool? result = null;
        dialogService.OnClose += value => result = (bool)value;
        OpenTrackedDialog(dialogService);

        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(parameters => parameters
            .Add(component => component.Message, "Delete Summer Camporee 2026?"));
        IElement confirmButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Delete", StringComparison.Ordinal));
        confirmButton.Click();

        Assert.Equal(true, result);
    }

    /// <remarks>See this class's own <c>&lt;remarks&gt;</c> for why this call is needed before <c>Close</c> does anything.</remarks>
    private static void OpenTrackedDialog(DialogService dialogService) =>
        dialogService.Open("Delete event?", typeof(ConfirmDialog), new Dictionary<string, object?>(), new DialogOptions());

    [Fact]
    public void RenderCustomConfirmText_WhenConfirmTextIsSet_ForConfirmDialog()
    {
        RadzenTestServices.RegisterRadzenComponentsHost(Services);

        IRenderedComponent<ConfirmDialog> cut = Render<ConfirmDialog>(parameters => parameters
            .Add(component => component.Message, "Remove Pat from this event?")
            .Add(component => component.ConfirmText, "Remove"));

        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Remove", StringComparison.Ordinal));
    }
}
