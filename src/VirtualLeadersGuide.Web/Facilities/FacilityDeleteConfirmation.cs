using VirtualLeadersGuide.Web.Components.Shared;

namespace VirtualLeadersGuide.Web.Facilities;

/// <summary>
/// The copy and <see cref="ConfirmDialog"/> parameters for deleting a Facility (P8-5, #169) - shared by
/// <c>FacilityList.razor.cs</c>'s grid row action and <c>FacilityEditor.razor.cs</c>'s delete action so the
/// two call sites can't drift on the consequence copy ADR-0045 governs.
/// </summary>
/// <remarks>
/// No cascade consequence bullet yet - nothing can exist under a Facility until the stories ADR-0081 lists ship,
/// and each of them adds its own bullet here, shown only when it applies (ADR-0045).
/// </remarks>
internal static class FacilityDeleteConfirmation
{
    /// <summary>The confirm dialog's title.</summary>
    public const string Title = "Delete facility?";

    /// <summary>Shown when the store fails the delete.</summary>
    public const string FailureMessage = "Something went wrong deleting this Facility. Try again.";

    /// <summary>Builds the parameters dictionary for <c>DialogService.OpenAsync&lt;ConfirmDialog&gt;</c> when confirming a Facility's deletion.</summary>
    /// <param name="name">The Facility's display Name, named in the dialog's message.</param>
    /// <returns>The dialog's parameters, keyed by <see cref="ConfirmDialog"/> property name.</returns>
    public static Dictionary<string, object?> BuildDialogParameters(string name) => new()
    {
        [nameof(ConfirmDialog.Message)] = $"Delete {name}?",
        [nameof(ConfirmDialog.Consequences)] = (IReadOnlyList<string>) ["This can't be undone"]
    };
}
