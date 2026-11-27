using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>An <see cref="InputText"/> that updates its bound value on every keystroke instead of on blur.</summary>
/// <remarks>
/// For a page whose Save button is disabled until something changed (P5-23, #206): with the stock
/// <see cref="InputText"/> the button would stay disabled until the field lost focus, and clicking a disabled
/// button doesn't reliably move focus. <c>InputText</c> hard-codes <c>onchange</c> and ignores
/// <c>@bind-Value:event</c>, hence the override; validation and its CSS classes still come from the base class.
/// </remarks>
public class ImmediateInputText : InputText
{
    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", CssClass);
        builder.AddAttribute(3, "value", CurrentValueAsString);
        builder.AddAttribute(4, "oninput",
            EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        builder.CloseElement();
    }
}
