using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Components.Shared;
using VirtualLeadersGuide.Web.Markdown;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Owns the Write/Preview behavior <c>ActivityEditorShould</c> and <c>InfoPageEditorShould</c> used to each
/// assert for themselves before ADR-0073 extracted the editor into <see cref="MarkdownField"/>. Those classes
/// keep one test apiece proving their own model is actually bound through it.
/// </remarks>
public class MarkdownFieldShould : BunitContext
{
    public MarkdownFieldShould()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<MarkdownRenderer>();
    }

    [Fact]
    public void RenderBothWriteAndPreviewPanes_RegardlessOfActivePane_ForRender()
    {
        IRenderedComponent<MarkdownField> cut = RenderField();

        Assert.Single(cut.FindAll(".md-pane-write"));
        Assert.Single(cut.FindAll(".md-pane-preview"));
    }

    [Fact]
    public void SwitchTheActivePaneAttribute_WhenPreviewIsSelected_ForRender()
    {
        IRenderedComponent<MarkdownField> cut = RenderField();

        Assert.Equal("Write", cut.Find(".md-panes").GetAttribute("data-active-pane"));

        cut.FindAll("button[role=radio]")
            .Single(item => item.TextContent.Contains("Preview", StringComparison.Ordinal))
            .Click();

        Assert.Equal("Preview", cut.Find(".md-panes").GetAttribute("data-active-pane"));
    }

    [Fact]
    public void RenderTheFieldIdAsTheTextareasIdAndLabelTarget_WhenRendering_ForRender()
    {
        IRenderedComponent<MarkdownField> cut = RenderField(fieldId: "Description", label: "Description");

        Assert.Equal("TEXTAREA", cut.Find("#Description").TagName);
        Assert.Equal("Description", cut.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void RenderTheSanitizedPreview_WhenValueIsSet_ForRender()
    {
        IRenderedComponent<MarkdownField> cut = RenderField(value: "**bold** <script>alert(1)</script>");

        Assert.Contains("<strong>bold</strong>", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".md-pane-preview script"));
    }

    /// <remarks>
    /// Regression coverage for the E2E-caught bug the InfoPage editor shipped with: every keystroke must raise
    /// <c>ValueChanged</c> and update the Preview, not only a blur/tab-away. <c>Input(...)</c> fires the DOM
    /// <c>input</c> event; <c>Change(...)</c> would have passed even with the bug present, since
    /// <c>onchange</c> was the default it came from.
    /// </remarks>
    [Fact]
    public void RaiseValueChangedAndUpdateThePreview_WhenTypingWithoutLeavingTheField_ForOnInputAsync()
    {
        string? raised = null;

        IRenderedComponent<MarkdownField> cut = Render<MarkdownField>(parameters => parameters
            .Add(component => component.Label, "Description")
            .Add(component => component.FieldId, "Description")
            .Add(component => component.Value, "")
            .Add(component => component.ValueChanged, (string? value) => raised = value));
        cut.Find("#Description").Input("**jacket**");

        Assert.Equal("**jacket**", raised);
        Assert.Contains("<strong>jacket</strong>", cut.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<MarkdownField> RenderField(string fieldId = "Field", string label = "Field", string? value = "") =>
        Render<MarkdownField>(parameters => parameters
            .Add(component => component.Label, label)
            .Add(component => component.FieldId, fieldId)
            .Add(component => component.Value, value));
}
