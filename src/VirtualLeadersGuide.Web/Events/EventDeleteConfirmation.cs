using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Events;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for deleting an Event (P2-17, #112) - shared by
/// <c>Dashboard.razor.cs</c>'s grid row action and <c>EventEditor.razor.cs</c>'s Danger zone so the two call
/// sites can't drift on the consequence copy ADR-0045 governs.
/// </summary>
internal static class EventDeleteConfirmation
{
    /// <summary>Builds the parameters dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c> when confirming an Event's deletion.</summary>
    /// <param name="eventName">The Event's display Name, named in the dialog's message.</param>
    /// <param name="slug">The Event's Slug, named in the address-frees-up-for-reuse consequence.</param>
    /// <param name="directorCount">
    /// The number of Directors with access to this Event, or <see langword="null"/> if that count couldn't be
    /// loaded - see <see cref="ConfirmDialog.Consequences"/>'s remarks (ADR-0045) for why a failure here
    /// degrades to explanatory text rather than blocking the dialog, and why a count of zero omits the bullet
    /// entirely rather than reading "0 directors."
    /// </param>
    public static Dictionary<string, object?> BuildDialogParameters(string eventName, string slug, int? directorCount)
    {
        List<string> consequences = [];
        if (directorCount is null)
        {
            consequences.Add("Directors with access couldn't be loaded");
        }
        else if (directorCount > 0)
        {
            string noun = directorCount == 1 ? "director" : "directors";
            string verb = directorCount == 1 ? "loses" : "lose";
            consequences.Add($"{directorCount} {noun} {verb} access to this event");
        }

        consequences.Add($"The address /e/{slug} frees up for reuse");
        consequences.Add("This can't be undone");

        return new Dictionary<string, object?>
        {
            [nameof(ConfirmDialog.Message)] = $"Delete {eventName}?",
            [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>)consequences
        };
    }
}
