using VirtualLeadersGuide.Web.Events;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Pure formatting logic, no bUnit/JS interop needed - <see cref="EventDateRange"/> takes its
/// <see cref="TimeZoneInfo"/> and "now" explicitly (its own remarks explain why), so every case here is
/// deterministic. This is also the one place the actual per-viewer timezone conversion is exercised
/// end-to-end (<c>DashboardRenderingShould</c>/<c>EventEditorShould</c> only cover the UTC fallback, per
/// ADR-0041's bUnit/JS-interop exemption) - see the same-instant-different-zones case below.
/// </remarks>
public class EventDateRangeShould
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now2026 = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReturnEmptyString_WhenBothDatesAreUnset_ForFormat()
    {
        string result = EventDateRange.Format(null, null, Utc, Now2026);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ReturnJustTheStart_WhenOnlyStartIsSet_ForFormat()
    {
        var startsAt = new DateTimeOffset(2026, 6, 12, 14, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.Format(startsAt, null, Utc, Now2026);

        Assert.Equal("JUN 12", result);
    }

    [Fact]
    public void OmitTheDash_WhenStartAndEndFallOnTheSameLocalDay_ForFormat()
    {
        var startsAt = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);
        var endsAt = new DateTimeOffset(2026, 6, 12, 17, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.Format(startsAt, endsAt, Utc, Now2026);

        Assert.Equal("JUN 12", result);
    }

    [Fact]
    public void JoinWithNoSpaces_WhenSameMonthAndYear_ForFormat()
    {
        var startsAt = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);
        var endsAt = new DateTimeOffset(2026, 6, 14, 17, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.Format(startsAt, endsAt, Utc, Now2026);

        Assert.Equal("JUN 12–14", result);
    }

    [Fact]
    public void JoinWithSpacedDash_WhenSameYearDifferentMonth_ForFormat()
    {
        var startsAt = new DateTimeOffset(2026, 6, 28, 9, 0, 0, TimeSpan.Zero);
        var endsAt = new DateTimeOffset(2026, 7, 2, 17, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.Format(startsAt, endsAt, Utc, Now2026);

        Assert.Equal("JUN 28 – JUL 2", result);
    }

    [Fact]
    public void ShowBothYears_WhenTheRangeCrossesAYearBoundary_ForFormat()
    {
        var startsAt = new DateTimeOffset(2026, 12, 30, 9, 0, 0, TimeSpan.Zero);
        var endsAt = new DateTimeOffset(2027, 1, 2, 17, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.Format(startsAt, endsAt, Utc, Now2026);

        Assert.Equal("DEC 30, 2026 – JAN 2, 2027", result);
    }

    [Fact]
    public void OmitTheYear_WhenTheDateFallsInTheViewersCurrentYear_ForFormat()
    {
        var startsAt = new DateTimeOffset(2026, 4, 19, 9, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.Format(startsAt, null, Utc, now);

        Assert.Equal("APR 19", result);
    }

    [Fact]
    public void IncludeTheYear_WhenTheDateFallsInAPastYear_ForFormat()
    {
        var startsAt = new DateTimeOffset(2025, 4, 19, 9, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.Format(startsAt, null, Utc, Now2026);

        Assert.Equal("APR 19, 2025", result);
    }

    /// <remarks>
    /// Pins the whole reason <see cref="EventDateRange.Format"/> takes a <see cref="TimeZoneInfo"/> at all
    /// (grilling decisions 5-7): the same UTC instant - 2am UTC, chosen so it falls on a different local
    /// calendar day in each zone - rendered for a US Eastern viewer (<c>minus5</c>, UTC-4/-5) and a Tokyo
    /// viewer (<c>plus9</c>, UTC+9) legitimately lands on different calendar days for each.
    /// </remarks>
    [Fact]
    public void RenderDifferentCalendarDays_WhenTheSameInstantIsFormattedForTwoDifferentZones_ForFormat()
    {
        var startsAt = new DateTimeOffset(2026, 6, 13, 2, 0, 0, TimeSpan.Zero);
        TimeZoneInfo minus5 = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        TimeZoneInfo plus9 = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

        string easternResult = EventDateRange.Format(startsAt, null, minus5, Now2026);
        string tokyoResult = EventDateRange.Format(startsAt, null, plus9, Now2026);

        Assert.Equal("JUN 12", easternResult);
        Assert.Equal("JUN 13", tokyoResult);
    }

    [Fact]
    public void ReturnNotSet_WhenStartIsUnset_ForFormatWithTime()
    {
        string result = EventDateRange.FormatWithTime(null, null, Utc);

        Assert.Equal("Not set", result);
    }

    [Fact]
    public void IncludeTimeOfDay_WhenStartIsSet_ForFormatWithTime()
    {
        var startsAt = new DateTimeOffset(2026, 6, 12, 14, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.FormatWithTime(startsAt, null, Utc);

        Assert.Equal("JUN 12, 2026 2:00 PM", result);
    }

    [Fact]
    public void JoinBothInstantsWithAnEnDash_WhenBothAreSet_ForFormatWithTime()
    {
        var startsAt = new DateTimeOffset(2026, 6, 12, 14, 0, 0, TimeSpan.Zero);
        var endsAt = new DateTimeOffset(2026, 6, 14, 22, 0, 0, TimeSpan.Zero);

        string result = EventDateRange.FormatWithTime(startsAt, endsAt, Utc);

        Assert.Equal("JUN 12, 2026 2:00 PM – JUN 14, 2026 10:00 PM", result);
    }
}
