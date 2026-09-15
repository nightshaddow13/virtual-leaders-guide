using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Components;

namespace VirtualLeadersGuide.Web.Markdown;

/// <summary>
/// Renders free-form markdown (InfoPage's <c>MarkdownContent</c>, CONTEXT.md's InfoPage entry) to sanitized
/// HTML - the one shared mechanism ADR-0048 asks for, built here for P5-17 (#22) and reused as-is by
/// Activity's Description once P5-8 (#94) wires it up.
/// </summary>
/// <remarks>
/// Markdown is stored raw, never as HTML - <c>InfoPage.MarkdownContent</c>'s own doc comment: "Sanitizing
/// happens at render time." Rendering happens here, not on Api, so a change to sanitization rules (or to the
/// Markdig extension set) never needs a data migration - every render re-applies the current rules to the
/// same stored source. Two layers, not one: Markdig's <c>DisableHtml()</c> escapes any raw HTML an author
/// types, but it does not filter URL schemes (a <c>[x](javascript:...)</c> link renders unless something
/// downstream removes it) and its Generic Attributes extension (part of <see cref="Markdig.MarkdownExtensions.UseAdvancedExtensions"/>)
/// lets an author attach arbitrary <c>key=value</c> HTML attributes via <c>{key=value}</c> syntax - so
/// <see cref="HtmlSanitizer"/> is the actual security boundary, not <c>DisableHtml()</c> alone, and runs
/// over Markdig's output regardless of which extensions produced it.
/// </remarks>
public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    /// <remarks>
    /// <see cref="HtmlSanitizer"/>'s default <c>AllowedTags</c> already covers every tag
    /// <c>UseAdvancedExtensions()</c>'s extensions can produce (tables, <c>dl</c>/<c>dt</c>/<c>dd</c>,
    /// <c>abbr</c>, <c>figure</c>/<c>figcaption</c>) and already excludes
    /// <c>script</c>/<c>style</c>/<c>iframe</c>/<c>object</c>/<c>embed</c> - verified against
    /// <c>HtmlSanitizerDefaults</c> before relying on it. The one default worth overriding is
    /// <c>AllowedSchemes</c>, which is <c>http</c>/<c>https</c> only by default - <c>mailto</c> is added so
    /// an ordinary contact-email link on an About/FAQ InfoPage isn't silently stripped.
    /// </remarks>
    private readonly HtmlSanitizer _sanitizer = new()
    {
        AllowedSchemes = { "http", "https", "mailto" }
    };

    /// <summary>Renders <paramref name="markdownContent"/> to sanitized HTML, safe to place in a <see cref="MarkupString"/>.</summary>
    /// <param name="markdownContent">Raw markdown, as stored on <c>InfoPage.MarkdownContent</c> - the empty string is legal.</param>
    /// <returns>The sanitized HTML, wrapped as a <see cref="MarkupString"/> ready for Blazor to render unescaped.</returns>
    public MarkupString RenderToHtml(string markdownContent)
    {
        string html = Markdig.Markdown.ToHtml(markdownContent, _pipeline);
        string sanitized = _sanitizer.Sanitize(html);
        return new MarkupString(sanitized);
    }
}
