using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Directors;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for deleting a User (P2-19, #114) - a pure function,
/// the same reasoning as <c>Events.EventDeleteConfirmation</c> and <c>DirectorRemovalConfirmation</c>.
/// </summary>
internal static class UserDeleteConfirmation
{
    /// <summary>Builds the parameters dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c> when confirming a User's deletion.</summary>
    /// <param name="userLabel">The User's display label (<c>DisplayName ?? Email</c>), named in the dialog's message.</param>
    /// <param name="eventGrantCount">The number of Event-scoped Grants this User holds.</param>
    /// <remarks>
    /// Takes a plain <see langword="int"/>, not <c>EventDeleteConfirmation</c>'s nullable <c>int?</c> - there
    /// is no degrade-to-explanatory-text case here (ADR-0045): <paramref name="eventGrantCount"/> comes from
    /// <see cref="UserRowDto.EventGrantCount"/> on the same fetch that renders the page at all, so a failure
    /// there lands on <c>UserDetail.razor.cs</c>'s <c>PageState.Denied</c> and this dialog never opens.
    /// </remarks>
    public static Dictionary<string, object?> BuildDialogParameters(string userLabel, int eventGrantCount)
    {
        List<string> consequences = [];
        if (eventGrantCount > 0)
        {
            string noun = eventGrantCount == 1 ? "event" : "events";
            consequences.Add($"They lose access to {eventGrantCount} {noun}");
        }

        consequences.Add("Their Director role and every event grant are removed");
        consequences.Add("This can't be undone");

        return new Dictionary<string, object?>
        {
            [nameof(ConfirmDialog.Message)] = $"Delete {userLabel}?",
            [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>)consequences
        };
    }
}
