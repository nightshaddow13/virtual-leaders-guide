namespace VirtualLeadersGuide.Web.Activities;

/// <summary>
/// A Placement's Tier path as names - Tab, then optionally Sub Tab, Section and Sub Section (P5-11, #96).
/// Equality is case-insensitive per segment, matching how Api resolves a typed name against an existing
/// Tier (ADR-0072) and how it decides two Placements are the same path (ADR-0046).
/// </summary>
/// <remarks>
/// Names rather than ids on purpose: a pending ghost row (decision 4) may reference a Tier that doesn't
/// exist on the server yet, so it has no id to carry. The two chains skip independently (CONTEXT.md): a
/// Section may be set with no Sub Tab. A Sub Section requires a Section on the same path.
/// </remarks>
public sealed class PlacementPath : IEquatable<PlacementPath>
{
    /// <summary>Builds a path, trimming each segment; a blank optional segment means "not set".</summary>
    /// <param name="tab">The required Tab name.</param>
    /// <param name="subTab">The Sub Tab name, or <see langword="null"/>/blank for none.</param>
    /// <param name="section">The Section name, or <see langword="null"/>/blank for none.</param>
    /// <param name="subSection">The Sub Section name, or <see langword="null"/>/blank for none - requires <paramref name="section"/>.</param>
    /// <exception cref="ArgumentException">The Tab is blank, or a Sub Section is given with no Section.</exception>
    public PlacementPath(string tab, string? subTab = null, string? section = null, string? subSection = null)
    {
        Tab = NormalizeRequired(tab);
        SubTab = NormalizeOptional(subTab);
        Section = NormalizeOptional(section);
        SubSection = NormalizeOptional(subSection);

        if (SubSection is not null && Section is null)
        {
            throw new ArgumentException("A Sub Section requires a Section.", nameof(subSection));
        }
    }

    /// <summary>The Tab name.</summary>
    public string Tab { get; }

    /// <summary>The Sub Tab name, if set.</summary>
    public string? SubTab { get; }

    /// <summary>The Section name, if set.</summary>
    public string? Section { get; }

    /// <summary>The Sub Section name, if set.</summary>
    public string? SubSection { get; }

    /// <summary>The set segments in order, for display.</summary>
    public IEnumerable<string> Segments => new[] { Tab, SubTab, Section, SubSection }.OfType<string>();

    /// <summary>The segments joined with a chevron - the resolved-path chip text (wireframe 1e-7).</summary>
    public string Display => string.Join(" › ", Segments);

    /// <inheritdoc/>
    public bool Equals(PlacementPath? other) =>
        other is not null
        && SameName(Tab, other.Tab) && SameName(SubTab, other.SubTab)
        && SameName(Section, other.Section) && SameName(SubSection, other.SubSection);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PlacementPath);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        Tab.ToUpperInvariant(), SubTab?.ToUpperInvariant(), Section?.ToUpperInvariant(), SubSection?.ToUpperInvariant());

    /// <summary>Whether two names are the same Tier name - case-insensitive, both-null counts as equal.</summary>
    /// <param name="left">One name, or <see langword="null"/>.</param>
    /// <param name="right">The other name, or <see langword="null"/>.</param>
    public static bool SameName(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRequired(string value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > 0 ? trimmed : throw new ArgumentException("A Tab is required.", nameof(value));
    }

    private static string? NormalizeOptional(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
