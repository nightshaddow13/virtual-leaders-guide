using System.ComponentModel.DataAnnotations;

namespace VirtualLeadersGuide.Web.Components.Shared;

/// <summary>An Activity's editable details, as bound by <see cref="ActivityDetailsFields"/>.</summary>
public sealed class ActivityFormModel
{
    /// <summary>The Activity's display name; required.</summary>
    /// <remarks>The length limit exists to stop an over-length Name reaching Api, which would 500, not 422, on this one field.</remarks>
    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(200, ErrorMessage = "Name can't be longer than 200 characters.")]
    public string? Name { get; set; }

    /// <summary>The Activity's markdown Description.</summary>
    /// <remarks>No <see cref="RequiredAttribute"/> - empty content is legal at the Api layer (an Activity is real and listable before it has a Description, CONTEXT.md's Activity entry).</remarks>
    public string? Description { get; set; }
}
