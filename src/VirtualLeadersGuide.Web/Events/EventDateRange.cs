using System.Globalization;

namespace VirtualLeadersGuide.Web.Events;

/// <summary>
/// Formats <see cref="EventDto.StartsAt"/>/<see cref="EventDto.EndsAt"/> for display (P2-15, #102) - the
/// one place this app turns those two UTC instants into text, so the dashboard grid and the Event editor
/// never drift into two different date formats.
/// </summary>
/// <remarks>
/// Both members take a <see cref="TimeZoneInfo"/> explicitly rather than reading one internally - every
/// viewer sees Event times converted to their own browser's timezone
/// (<see cref="VirtualLeadersGuide.Web.Time.BrowserTimeZoneAccessor"/>), never the offset they were entered
/// with, so the caller (a page's code-behind) is the one that already resolved which zone applies. Callers
/// also pass "now" explicitly rather than this type reading <see cref="DateTimeOffset.UtcNow"/> itself, so
/// the year-omission rule is deterministic under test.
/// </remarks>
public static class EventDateRange
{
    /// <summary>
    /// The dashboard grid's compact, date-only rendering - e.g. <c>JUN 12–14</c>, or <c>APR 19, 2025</c> when
    /// the year isn't <paramref name="now"/>'s in <paramref name="viewerZone"/>.
    /// </summary>
    /// <param name="startsAt">The Event's start, in UTC.</param>
    /// <param name="endsAt">The Event's end, in UTC, or <see langword="null"/>.</param>
    /// <param name="viewerZone">The viewer's own timezone to render both instants in.</param>
    /// <param name="now">The current instant, used only to decide whether to omit the year.</param>
    /// <returns>
    /// An empty string when <paramref name="startsAt"/> is <see langword="null"/> (CONTEXT.md's Starts at /
    /// Ends at entry: unset dates render blank, never an error or a default). Otherwise the formatted range.
    /// </returns>
    public static string Format(DateTimeOffset? startsAt, DateTimeOffset? endsAt, TimeZoneInfo viewerZone, DateTimeOffset now)
    {
        if (startsAt is null)
        {
            return string.Empty;
        }

        DateTimeOffset start = TimeZoneInfo.ConvertTime(startsAt.Value, viewerZone);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, viewerZone);

        if (endsAt is not { } endsAtValue)
        {
            return FormatSingle(start, localNow);
        }

        DateTimeOffset end = TimeZoneInfo.ConvertTime(endsAtValue, viewerZone);

        if (IsSameLocalDay(start, end))
        {
            return FormatSingle(start, localNow);
        }

        if (start.Year != end.Year)
        {
            return $"{MonthDay(start)}, {start.Year} – {MonthDay(end)}, {end.Year}";
        }

        string yearSuffix = YearSuffix(start, localNow);
        return start.Month == end.Month
            ? $"{MonthAbbreviation(start)} {start.Day}–{end.Day}{yearSuffix}"
            : $"{MonthDay(start)} – {MonthDay(end)}{yearSuffix}";
    }

    /// <summary>
    /// The Event editor's full rendering, with time of day - e.g. <c>JUN 12, 2026 9:00 AM</c>, or a range
    /// joined with an en dash when both are set. Used by the Director's read-only detail view; the Admin
    /// form shows time through its own <c>datetime-local</c> inputs instead.
    /// </summary>
    /// <param name="startsAt">The Event's start, in UTC.</param>
    /// <param name="endsAt">The Event's end, in UTC, or <see langword="null"/>.</param>
    /// <param name="viewerZone">The viewer's own timezone to render both instants in.</param>
    /// <returns><c>"Not set"</c> when <paramref name="startsAt"/> is <see langword="null"/>, otherwise the formatted instant(s).</returns>
    public static string FormatWithTime(DateTimeOffset? startsAt, DateTimeOffset? endsAt, TimeZoneInfo viewerZone)
    {
        if (startsAt is null)
        {
            return "Not set";
        }

        DateTimeOffset start = TimeZoneInfo.ConvertTime(startsAt.Value, viewerZone);
        string startText = MonthDayTime(start);

        if (endsAt is not { } endsAtValue)
        {
            return startText;
        }

        DateTimeOffset end = TimeZoneInfo.ConvertTime(endsAtValue, viewerZone);
        return $"{startText} – {MonthDayTime(end)}";
    }

    private static bool IsSameLocalDay(DateTimeOffset start, DateTimeOffset end) =>
        start.Year == end.Year && start.Month == end.Month && start.Day == end.Day;

    private static string FormatSingle(DateTimeOffset date, DateTimeOffset localNow) =>
        $"{MonthDay(date)}{YearSuffix(date, localNow)}";

    private static string YearSuffix(DateTimeOffset date, DateTimeOffset localNow) =>
        date.Year == localNow.Year ? string.Empty : $", {date.Year}";

    private static string MonthDay(DateTimeOffset date) => $"{MonthAbbreviation(date)} {date.Day}";

    private static string MonthDayTime(DateTimeOffset date) =>
        $"{MonthAbbreviation(date)} {date.Day}, {date.Year} {date.ToString("h:mm tt", CultureInfo.InvariantCulture)}";

    private static string MonthAbbreviation(DateTimeOffset date) =>
        date.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
}
