using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Components.PublicGuide;

/// <summary>
/// One screen of the public guide: an InfoPage, or Activities under their Section and Sub Section headings,
/// never both (ADR-0047; wireframes 3a, 3c).
/// </summary>
public partial class GuideScreen
{
    [Inject]
    private MarkdownRenderer MarkdownRenderer { get; set; } = default!;

    /// <summary>What to show.</summary>
    [Parameter, EditorRequired]
    public PublicGuideScreenDto Screen { get; set; } = default!;
}
