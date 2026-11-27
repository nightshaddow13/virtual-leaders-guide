using Microsoft.AspNetCore.Components;
using Radzen;
using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// Opens one Event's Leaders Guide in a new tab (P2-23, #213). Disabled, with a tooltip saying why, while the
/// Event is a Draft.
/// </summary>
/// <remarks>
/// A real anchor rather than a <c>RadzenButton</c>, per ADR-0037's amendment. The disabled form needs ADR-0052's
/// wrapper element to own the hover; one component per row means each owns its own reference to it. Draft stays
/// disabled until staff can preview a Draft Event's guide (#196).
/// </remarks>
public partial class GuideLinkButton
{
    internal const string AdminDraftTooltipText =
        "Draft guides can't be previewed yet. Go live to open this guide.";

    internal const string DirectorDraftTooltipText =
        "Draft guides can't be previewed yet. It opens once an Admin takes it live.";

    private ElementReference anchor;

    [Inject]
    private TooltipService TooltipService { get; set; } = default!;

    /// <summary>The Event's address, as in <c>/e/{slug}</c>.</summary>
    [Parameter, EditorRequired]
    public string Slug { get; set; } = "";

    /// <summary>The Event's Status; only <see cref="EventStatus.Draft"/> disables the action.</summary>
    [Parameter, EditorRequired]
    public EventStatus Status { get; set; }

    /// <summary>Whether the viewer is an Admin, which picks the Draft tooltip's wording.</summary>
    [Parameter, EditorRequired]
    public bool IsAdmin { get; set; }

    private void ShowDraftTooltip() =>
        TooltipService.Open(anchor, IsAdmin ? AdminDraftTooltipText : DirectorDraftTooltipText);
}
