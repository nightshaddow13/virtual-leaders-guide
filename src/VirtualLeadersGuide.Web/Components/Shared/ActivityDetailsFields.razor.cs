using Microsoft.AspNetCore.Components;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>
/// The Name and Description inputs for an Activity, shared by the new-Activity form and the Activity page
/// (P5-23, #206). Place it inside an <c>EditForm</c> whose model is <see cref="Model"/>.
/// </summary>
public partial class ActivityDetailsFields
{
    /// <summary>The values being edited; changed in place.</summary>
    [Parameter, EditorRequired]
    public ActivityFormModel Model { get; set; } = default!;

    /// <summary>Raised after either value changes, so a host that shows whether anything is unsaved re-renders.</summary>
    [Parameter]
    public EventCallback Changed { get; set; }

    private Task OnNameChangedAsync(string? value)
    {
        Model.Name = value;
        return Changed.InvokeAsync();
    }

    private Task OnDescriptionChangedAsync(string? value)
    {
        Model.Description = value;
        return Changed.InvokeAsync();
    }
}
