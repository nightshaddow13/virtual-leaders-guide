using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Pure unit tests - no bUnit needed, <see cref="MarkdownRenderer"/> takes no dependencies. Covers the
/// grilled decisions (P5-17, #22): Markdig's <c>UseAdvancedExtensions()</c> bundle, and the two-layer
/// security design where <see cref="Ganss.Xss.HtmlSanitizer"/>, not Markdig's <c>DisableHtml()</c> alone, is
/// what actually closes the XSS concern ADR-0048 names.
/// </remarks>
public class MarkdownRendererShould
{
    [Fact]
    public void RenderHeadingsBoldAndItalic_WhenGivenCommonMarkConstructs_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("# Heading\n\n**bold** and *italic*.").Value!;

        Assert.Contains("<h1", html, StringComparison.Ordinal);
        Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>italic</em>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderLists_WhenGivenCommonMarkConstructs_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("- fleece\n- rain shell").Value!;

        Assert.Contains("<ul>", html, StringComparison.Ordinal);
        Assert.Contains("<li>fleece</li>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderATable_WhenGivenPipeTableSyntax_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("| A | B |\n|---|---|\n| 1 | 2 |").Value!;

        Assert.Contains("<table>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAnAutolinkedUrl_WhenGivenABareUrl_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("Visit https://example.org for details.").Value!;

        Assert.Contains("<a href=\"https://example.org\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PreserveAMailtoLink_WhenGivenAMailtoMarkdownLink_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("[Contact us](mailto:info@example.org)").Value!;

        Assert.Contains("href=\"mailto:info@example.org\"", html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// <c>DisableHtml()</c> escapes raw HTML rather than dropping it - the tag survives only as inert,
    /// visible text (<c>&amp;lt;script&amp;gt;</c>), never as a live element a browser would execute. That's
    /// the safe outcome this test pins: no live <c>&lt;script&gt;</c> tag, whatever became of the text inside it.
    /// </remarks>
    [Fact]
    public void EscapeAScriptTagRatherThanExecuteIt_WhenRawHtmlIsEmbedded_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("Hello <script>alert('xss')</script> world").Value!;

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void StripAJavascriptSchemeLink_WhenGivenAMarkdownLinkWithAJavascriptHref_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("[click me](javascript:alert(1))").Value!;

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// Pins the grilled finding that Markdig's Generic Attributes extension (part of
    /// <c>UseAdvancedExtensions()</c>) can't be used to smuggle an event-handler attribute past
    /// <c>DisableHtml()</c> - <see cref="Ganss.Xss.HtmlSanitizer"/>'s default attribute allow-list strips it
    /// regardless of which Markdig extension produced it.
    /// </remarks>
    [Fact]
    public void StripAnOnClickAttribute_WhenGivenGenericAttributeSyntax_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("# Heading {onclick=alert(1)}").Value!;

        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderEmptyOutput_WhenGivenAnEmptyString_ForRenderToHtml()
    {
        var renderer = new MarkdownRenderer();

        string html = renderer.RenderToHtml("").Value!;

        Assert.Equal("", html);
    }
}
