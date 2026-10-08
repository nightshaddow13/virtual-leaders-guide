using Microsoft.AspNetCore.Components;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// A labelled markdown authoring field: a Write textarea beside a live, sanitized Preview of what's typed,
/// shared by every editor that authors markdown (<c>InfoPageEditor</c>'s content, <c>ActivityEditor</c>'s
/// Description) so a change to the editing surface is made once (ADR-0073, ADR-0048).
/// </summary>
/// <remarks>
/// Bind it with <c>@bind-Value</c>. Both panes always render in the DOM and CSS alone decides which shows -
/// side by side at 64rem and wider, one at a time below it via the Write/Preview toggle - so there is no JS
/// interop and <see cref="activePane"/> is all the state the stylesheet needs.
/// </remarks>
public partial class MarkdownField
{
    [Inject]
    private MarkdownRenderer MarkdownRenderer { get; set; } = default!;

    /// <summary>The visible label above the field.</summary>
    [Parameter, EditorRequired]
    public string Label { get; set; } = "";

    /// <summary>
    /// The textarea's element id. Callers keep their own existing ids (<c>Description</c>,
    /// <c>MarkdownContent</c>) because E2E tests locate the field by them.
    /// </summary>
    [Parameter, EditorRequired]
    public string FieldId { get; set; } = "";

    /// <summary>The raw markdown.</summary>
    /// <remarks>
    /// Bound to a plain native <c>&lt;textarea&gt;</c> listening to <c>oninput</c>, deliberately not the
    /// <c>InputTextArea</c> component. <c>InputTextArea</c> hard-codes its own <c>onchange</c> binding in its
    /// <c>BuildRenderTree</c> and has no parameter a caller's <c>@bind-Value:event</c> can override - a first
    /// attempt using it with <c>@bind-Value:event="oninput"</c> silently compiled but did nothing, since
    /// Razor's <c>:event</c> customization only overrides which DOM event a native element binds to, not a
    /// component's own internal wiring. The point of a live Preview is that it reflects what's being typed,
    /// not only what was typed the last time the field lost focus (E2E-caught, which fills the field and
    /// checks the Preview without ever blurring it, as a real author glancing at Preview mid-sentence would).
    /// </remarks>
    [Parameter]
    public string? Value { get; set; }

    /// <summary>Raised on every keystroke with the new raw markdown.</summary>
    [Parameter]
    public EventCallback<string?> ValueChanged { get; set; }

    /// <remarks>Only meaningful below the 64rem breakpoint; ignored above it, where both panes always show.</remarks>
    private enum PaneView { Write, Preview }

    private PaneView activePane = PaneView.Write;

    private Task OnInputAsync(ChangeEventArgs args)
    {
        Value = args.Value?.ToString();
        return ValueChanged.InvokeAsync(Value);
    }
}
