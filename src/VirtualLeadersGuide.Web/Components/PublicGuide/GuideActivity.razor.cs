using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Identity.Contracts;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Components.PublicGuide;

/// <summary>One Activity on a public guide screen: its name and rendered description.</summary>
public partial class GuideActivity
{
    [Inject]
    private MarkdownRenderer MarkdownRenderer { get; set; } = default!;

    /// <summary>The Activity to show.</summary>
    [Parameter, EditorRequired]
    public PublicGuideActivityDto Activity { get; set; } = default!;

    /// <summary>Whether a Sub Section heading sits above it, which puts its name one heading level lower.</summary>
    [Parameter]
    public bool InSubSection { get; set; }
}
