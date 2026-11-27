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

    /// <summary><see cref="Name"/> as it would be sent to Api.</summary>
    public string NameOrEmpty => Name ?? string.Empty;

    /// <summary><see cref="Description"/> as it would be sent to Api.</summary>
    public string DescriptionOrEmpty => Description ?? string.Empty;

    /// <summary>Replaces both values.</summary>
    /// <param name="name">The saved Name.</param>
    /// <param name="description">The saved Description.</param>
    public void Load(string name, string description)
    {
        Name = name;
        Description = description;
    }

    /// <summary>Whether either value no longer matches what's saved.</summary>
    /// <param name="name">The saved Name.</param>
    /// <param name="description">The saved Description.</param>
    public bool Differs(string name, string description) => NameOrEmpty != name || DescriptionOrEmpty != description;
}
