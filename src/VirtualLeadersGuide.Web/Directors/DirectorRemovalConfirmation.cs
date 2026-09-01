using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for removing a Director's access to an Event (P2-18,
/// #113) - a pure function, the same reasoning as <c>Events.EventDeleteConfirmation</c>: nothing in this
/// suite stubs <see cref="Radzen.DialogService.OpenAsync{T}(string, System.Collections.Generic.Dictionary{string, object}?, Radzen.DialogOptions?)"/>,
/// so the parameters this call builds are what's actually tested.
/// </summary>
internal static class DirectorRemovalConfirmation
{
    /// <summary>Builds the parameters dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c> when confirming a Director's removal from an Event.</summary>
    /// <param name="directorLabel">The Director's display label (<see cref="EventDirectorDto.DisplayLabel"/>), named in the dialog's message.</param>
    /// <param name="eventName">The Event's display Name, named in the dialog's message.</param>
    /// <remarks>
    /// Every bullet is unconditional, unlike <c>EventDeleteConfirmation</c>'s Director-count bullet - none of
    /// them depends on a fallible fetch, so there is no degrade-to-explanatory-text case here (ADR-0045).
    /// Deliberately omitted: AC3's internal-JWT lag (a removed Director may keep read access until their
    /// session refreshes) - the Admin can't act on it, and surfacing it would make every removal look
    /// unreliable for no benefit; it stays documented on <c>EventAccessPolicy</c> where it already lives.
    /// </remarks>
    public static Dictionary<string, object?> BuildDialogParameters(string directorLabel, string eventName)
    {
        List<string> consequences =
        [
            "They lose access to this event",
            "They keep the Director role and any other events they hold",
            "You can add them back from this page at any time"
        ];

        return new Dictionary<string, object?>
        {
            [nameof(ConfirmDialog.Message)] = $"Remove {directorLabel} from {eventName}?",
            [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>)consequences,
            [nameof(ConfirmDialog.ConfirmText)] = "Remove"
        };
    }
}
