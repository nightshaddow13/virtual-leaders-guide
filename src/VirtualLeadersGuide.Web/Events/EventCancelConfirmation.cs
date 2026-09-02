using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Events;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for cancelling a Live Event (P2-20, #115) - mirrors
/// <see cref="EventDeleteConfirmation"/>'s shape so the two call sites in <c>EventEditor.razor.cs</c>'s Danger
/// zone can't drift on copy.
/// </summary>
internal static class EventCancelConfirmation
{
    /// <summary>Builds the parameters dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c> when confirming an Event's cancellation.</summary>
    /// <param name="eventName">The Event's display Name, named in the dialog's message.</param>
    /// <param name="directorCount">
    /// The number of Directors with access to this Event, or <see langword="null"/> if that count couldn't be
    /// loaded - see <see cref="ConfirmDialog.Consequences"/>'s remarks (ADR-0045) for the same
    /// degrade-not-block convention <see cref="EventDeleteConfirmation"/> already follows.
    /// </param>
    public static Dictionary<string, object?> BuildDialogParameters(string eventName, int? directorCount)
    {
        List<string> consequences = [];
        if (directorCount is null)
        {
            consequences.Add("Directors with access couldn't be loaded");
        }
        else if (directorCount > 0)
        {
            string noun = directorCount == 1 ? "director" : "directors";
            consequences.Add($"{directorCount} assigned {noun} will see it as cancelled");
        }

        consequences.Add("Its public Leaders Guide stops serving the guide");
        consequences.Add("It drops off the default Events list");
        consequences.Add("It can't be un-cancelled - Duplicate is the only way to start over");

        return new Dictionary<string, object?>
        {
            [nameof(ConfirmDialog.Message)] = $"Cancel {eventName}?",
            [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>)consequences,
            [nameof(ConfirmDialog.ConfirmText)] = "Cancel event",
            [nameof(ConfirmDialog.DismissText)] = "Keep it live"
        };
    }
}
