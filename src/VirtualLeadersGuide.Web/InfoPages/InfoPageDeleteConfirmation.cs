using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.InfoPages;

/// <summary>
/// Builds the <see cref="ConfirmDialog"/> parameters for deleting an InfoPage (P5-17, #22) - shared by
/// <c>InfoPageList.razor.cs</c>'s grid row action and <c>InfoPageEditor.razor.cs</c>'s delete action so the
/// two call sites can't drift on the consequence copy ADR-0045 governs.
/// </summary>
/// <remarks>
/// No Placement-related consequence bullet - P5-18/19/20 (#86/#91/#92), which add Placement, all depend on
/// this ticket and haven't shipped, so there's nothing placed yet to warn about losing.
/// </remarks>
internal static class InfoPageDeleteConfirmation
{
    /// <summary>Builds the parameters dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c> when confirming an InfoPage's deletion.</summary>
    /// <param name="title">The InfoPage's display Title, named in the dialog's message.</param>
    public static Dictionary<string, object?> BuildDialogParameters(string title) => new()
    {
        [nameof(ConfirmDialog.Message)] = $"Delete {title}?",
        [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>) ["This can't be undone"]
    };
}
