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
public static class Slug
{
    // Matches Event.Slug's column length (VirtualLeadersGuideDbContext) - truncating here rather than letting
    // an oversized candidate reach the database and fail the CHECK constraint / column length there instead.
    private const int MaxLength = 100;

    /// <summary>
    /// Converts <paramref name="name"/> into a lowercase, hyphen-separated, URL-safe candidate Slug:
    /// diacritics are stripped (<c>"Café"</c> → <c>"cafe"</c>), runs of non-alphanumeric characters collapse
    /// to a single hyphen, leading/trailing hyphens are trimmed, and the result is truncated to fit the
    /// <see cref="Event.Slug"/> column. Returns <see cref="string.Empty"/> when nothing survives (e.g.
    /// <paramref name="name"/> is entirely punctuation/whitespace) - the caller decides what to do with that;
    /// this helper doesn't invent a fallback value.
    /// </summary>
    public static string From(string name)
    {
        string decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool lastWasHyphen = false;

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                // A diacritic mark FormD split off its base letter (e.g. the acute accent on "é") - the base
                // letter was already appended below, so just drop the mark instead of turning it into a hyphen.
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
