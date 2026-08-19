using System.Globalization;
using System.Text;

namespace VirtualLeadersGuide.Api.Data;

/// <summary>
/// Derives a URL-safe starting value for <see cref="Event.Slug"/> from an Event's <see cref="Event.Name"/>
/// (CONTEXT.md's Slug entry - "auto-derived from Name but editable"). A pure string transform: it never
/// touches the database, so two Events whose Names derive to the same Slug will only ever discover the
/// collision when the second save trips the unique index - resolved by the Admin editing the Slug by hand,
/// not by this helper silently appending a suffix on their behalf (a route the Admin never chose).
/// </summary>
/// <remarks>
/// Named <c>SlugDerivation</c>, not <c>Slug</c> - a static class named exactly "Slug" collided with
/// <see cref="Event.Slug"/> (the property) badly enough to force fully-qualified references and an
/// explanatory comment at every call site (see <see cref="Event.Create"/>). Renaming the type instead of
/// working around the collision at each use.
/// </remarks>
public static class SlugDerivation
{
    /// <remarks>
    /// Matches <see cref="Event.Slug"/>'s column length (<see cref="VirtualLeadersGuideDbContext"/>) -
    /// truncating here rather than letting an oversized candidate reach the database and fail the CHECK
    /// constraint / column length there instead.
    /// </remarks>
    private const int MaxLength = 100;

    /// <summary>
    /// Converts <paramref name="name"/> into a lowercase, hyphen-separated, URL-safe candidate Slug:
    /// diacritics are stripped (<c>"Café"</c> → <c>"cafe"</c>), runs of non-alphanumeric characters collapse
    /// to a single hyphen, leading/trailing hyphens are trimmed, and the result is truncated to fit the
    /// <see cref="Event.Slug"/> column.
    /// </summary>
    /// <param name="name">The Event Name to derive a candidate Slug from.</param>
    /// <returns>
    /// The derived candidate Slug, or <see cref="string.Empty"/> when nothing survives (e.g.
    /// <paramref name="name"/> is entirely punctuation/whitespace) - the caller decides what to do with that;
    /// this helper doesn't invent a fallback value.
    /// </returns>
    /// <remarks>
    /// A diacritic mark FormD splits off its base letter (e.g. the acute accent on "é") - the base letter is
    /// appended on its own iteration, so a <see cref="UnicodeCategory.NonSpacingMark"/> is simply dropped
    /// rather than turned into a hyphen.
    /// </remarks>
    public static string From(string name)
    {
        string decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool lastWasHyphen = false;

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        if (lastWasHyphen)
        {
            builder.Length--;
        }

        string slug = builder.ToString();
        return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
    }
}
