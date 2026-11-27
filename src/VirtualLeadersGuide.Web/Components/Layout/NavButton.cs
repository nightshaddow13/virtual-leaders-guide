namespace VirtualLeadersGuide.Web.Components.Layout;

/// <summary>The classes that dress the top bar's real links, <c>&lt;summary&gt;</c> buttons and menus in Radzen's styles.</summary>
/// <remarks>Markup rather than <c>RadzenButton</c> because the bar has no circuit (ADR-0034). Selection is a Radzen variant, so it needs no CSS of ours.</remarks>
public static class NavButton
{
    /// <summary>An entry inside a menu: left-aligned.</summary>
    public const string MenuItem = "rz-justify-content-start rz-text-align-start";

    /// <summary>The panel a menu opens; its position, border and background are the shared <c>vlg-menu-panel</c> rule.</summary>
    public const string Panel = "vlg-menu-panel rz-display-flex rz-flex-column rz-align-items-stretch rz-p-1 rz-overflow-auto";

    /// <summary>The class list for one entry.</summary>
    /// <param name="isSelected">Whether the entry is where the visitor currently is.</param>
    /// <returns>The classes to put on the element.</returns>
    public static string Classes(bool isSelected = false) =>
        $"rz-button rz-button-sm rz-shade-default rz-primary {(isSelected ? "rz-variant-flat" : "rz-variant-text")}";

    /// <summary>The class list for a menu's own <c>&lt;summary&gt;</c> button.</summary>
    /// <param name="isSelected">Whether the current screen is somewhere inside the menu.</param>
    /// <returns><see cref="Classes"/>, laid out so it lines up with the plain links beside it.</returns>
    public static string Summary(bool isSelected = false) => $"{Classes(isSelected)} rz-display-flex rz-text-nowrap";
}
